using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Hedron.Core.Events;
using Hedron.Core.Modules.Account.Events;
using Hedron.Core.Modules.Account.Systems;
using Hedron.Core.Output;
using Hedron.Core.Sessions;
using Hedron.Core.Systems;
using Microsoft.Extensions.Configuration;

namespace Hedron.Server.Sessions
{
    /// <summary>
    /// Initiator — drives the login state machine: welcome banner → new account or
    /// authenticate → character creation or selection → returns a <see cref="LoginResult"/>
    /// to <c>TelnetSession</c> for world-entry binding.
    ///
    /// Lives in Server/Sessions/ because the state machine is transport-coupled (reads raw
    /// lines). Domain logic delegates to <see cref="IAccountSystem"/>.
    /// </summary>
    internal sealed class LoginFlow
    {
        private readonly ISession _session;
        private readonly StreamReader _reader;
        private readonly IAccountSystem _accountSystem;
        private readonly IOutputWriterFactory _outputWriterFactory;
        private readonly IEventBus _eventBus;
        private readonly IPersistenceSystem _persistence;
        private readonly int _maxCharacters;

        private const int MaxLoginAttempts = 3;

        public LoginFlow(
            ISession session,
            StreamReader reader,
            IAccountSystem accountSystem,
            IOutputWriterFactory outputWriterFactory,
            IEventBus eventBus,
            IPersistenceSystem persistence,
            IConfiguration configuration)
        {
            _session = session;
            _reader = reader;
            _accountSystem = accountSystem;
            _outputWriterFactory = outputWriterFactory;
            _eventBus = eventBus;
            _persistence = persistence;
            _maxCharacters = configuration.GetValue<int>("Account:MaxCharactersPerAccount", 5);
        }

        /// <returns>
        /// A <see cref="LoginResult"/> once a character is bound, or <c>null</c> if the
        /// client disconnected or exceeded the login attempt limit.
        /// </returns>
        public async Task<LoginResult?> RunAsync(CancellationToken ct)
        {
            var output = _outputWriterFactory.Create(_session);

            await output.WriteAsync(new PlainMessage(
                "Welcome to Hedron.\nDo you have an existing account? (yes/no)",
                OutputSeverity.System, OutputCategory.Notification)).ConfigureAwait(false);

            var choice = await ReadLineAsync(ct).ConfigureAwait(false);
            if (choice is null) return null;

            return IsYes(choice)
                ? await RunAuthFlowAsync(output, ct).ConfigureAwait(false)
                : await RunRegistrationFlowAsync(output, ct).ConfigureAwait(false);
        }

        // ── Registration ──────────────────────────────────────────────────────────────

        private async Task<LoginResult?> RunRegistrationFlowAsync(IOutputWriter output, CancellationToken ct)
        {
            var username = await PromptValidatedUsernameAsync(output, ct, mustBeNew: true)
                .ConfigureAwait(false);
            if (username is null) return null;

            var password = await PromptPasswordWithConfirmationAsync(output, ct)
                .ConfigureAwait(false);
            if (password is null) return null;

            var accountId = await _accountSystem.CreateAccountAsync(username, password, ct)
                .ConfigureAwait(false);

            await output.WriteAsync(new PlainMessage(
                "Account created. Let's create your first character.",
                OutputSeverity.System, OutputCategory.Notification)).ConfigureAwait(false);

            // Character creation + saves + event publishing are handled together so that
            // character is written before account (crash-safety) and both events publish
            // only after both entities are on disk.
            return await RunCharacterCreationFlowAsync(output, accountId, ct, newAccountUsername: username)
                .ConfigureAwait(false);
        }

        // ── Authentication ─────────────────────────────────────────────────────────────

        private async Task<LoginResult?> RunAuthFlowAsync(IOutputWriter output, CancellationToken ct)
        {
            for (var attempt = 0; attempt < MaxLoginAttempts; attempt++)
            {
                var username = await PromptValidatedUsernameAsync(output, ct, mustBeNew: false)
                    .ConfigureAwait(false);
                if (username is null) return null;

                await output.WriteAsync(new PlainMessage("Password:", OutputSeverity.System, OutputCategory.Notification))
                    .ConfigureAwait(false);
                var password = await ReadLineAsync(ct).ConfigureAwait(false);
                if (password is null) return null;

                var result = await _accountSystem.AuthenticateAsync(username, password, ct)
                    .ConfigureAwait(false);

                if (result.Success)
                    return await RunCharacterSelectionAsync(output, result.AccountEntityId, ct)
                        .ConfigureAwait(false);

                var remaining = MaxLoginAttempts - attempt - 1;
                if (remaining > 0)
                    await output.WriteAsync(new PlainMessage(
                        $"Invalid username or password. {remaining} attempt(s) remaining.",
                        OutputSeverity.Error, OutputCategory.Notification)).ConfigureAwait(false);
            }

            await output.WriteAsync(new PlainMessage(
                "Too many failed attempts. Disconnecting.", OutputSeverity.Error, OutputCategory.Notification))
                .ConfigureAwait(false);
            return null;
        }

        // ── Character selection ────────────────────────────────────────────────────────

        private async Task<LoginResult?> RunCharacterSelectionAsync(
            IOutputWriter output, uint accountId, CancellationToken ct)
        {
            var characters = _accountSystem.GetCharacterList(accountId);

            if (characters.Count == 0)
            {
                await output.WriteAsync(new PlainMessage(
                    "No characters found. Let's create one.", OutputSeverity.System, OutputCategory.Notification))
                    .ConfigureAwait(false);
                return await RunCharacterCreationFlowAsync(output, accountId, ct).ConfigureAwait(false);
            }

            while (true)
            {
                await output.WriteAsync(new PlainMessage(
                    BuildRoster(characters), OutputSeverity.System, OutputCategory.Notification)).ConfigureAwait(false);

                var choice = await ReadLineAsync(ct).ConfigureAwait(false);
                if (choice is null) return null;
                choice = choice.Trim();

                if (string.Equals(choice, "new", StringComparison.OrdinalIgnoreCase))
                {
                    if (characters.Count >= _maxCharacters)
                    {
                        await output.WriteAsync(new PlainMessage(
                            $"You have reached the maximum of {_maxCharacters} characters.",
                            OutputSeverity.Error, OutputCategory.Notification)).ConfigureAwait(false);
                        continue;
                    }
                    return await RunCharacterCreationFlowAsync(output, accountId, ct).ConfigureAwait(false);
                }

                if (int.TryParse(choice, out var index) && index >= 1 && index <= characters.Count)
                {
                    var selected = characters[index - 1];
                    return new LoginResult(selected.CharacterEntityId, accountId, selected.CharacterName);
                }

                await output.WriteAsync(new PlainMessage(
                    "Please enter a number from the list, or 'new' to create a character.",
                    OutputSeverity.Error, OutputCategory.Notification)).ConfigureAwait(false);
            }
        }

        // ── Character creation ─────────────────────────────────────────────────────────

        /// <param name="newAccountUsername">
        /// Non-null during registration: the new account's username. Causes
        /// <c>AccountCreatedEvent</c> to be published after saves complete.
        /// </param>
        private async Task<LoginResult?> RunCharacterCreationFlowAsync(
            IOutputWriter output, uint accountId, CancellationToken ct,
            string? newAccountUsername = null)
        {
            // TODO: future — add 'delete' option here (character deletion is out of scope for slice 5)
            while (true)
            {
                await output.WriteAsync(new PlainMessage(
                    "Enter a name for your character (2–16 letters):",
                    OutputSeverity.System, OutputCategory.Notification)).ConfigureAwait(false);

                var name = await ReadLineAsync(ct).ConfigureAwait(false);
                if (name is null) return null;
                name = name.Trim();

                var error = ValidateCharacterName(name);
                if (error is not null)
                {
                    await output.WriteAsync(new PlainMessage(error, OutputSeverity.Error, OutputCategory.Notification))
                        .ConfigureAwait(false);
                    continue;
                }

                var charId = await _accountSystem.CreateCharacterAsync(accountId, name, ct)
                    .ConfigureAwait(false);

                // Character written before account: if the server crashes between the two writes,
                // an orphaned character file is recoverable; a dangling account pointer to a
                // missing character file is more harmful.
                await _persistence.SaveEntityAsync(charId, ct).ConfigureAwait(false);
                await _persistence.SaveEntityAsync(accountId, ct).ConfigureAwait(false);

                if (newAccountUsername is not null)
                    await _eventBus.PublishAsync(new AccountCreatedEvent(accountId, newAccountUsername))
                        .ConfigureAwait(false);

                await _eventBus.PublishAsync(new CharacterCreatedEvent(charId, accountId, name))
                    .ConfigureAwait(false);

                return new LoginResult(charId, accountId, name);
            }
        }

        // ── Input helpers ──────────────────────────────────────────────────────────────

        private async Task<string?> PromptValidatedUsernameAsync(
            IOutputWriter output, CancellationToken ct, bool mustBeNew)
        {
            while (true)
            {
                await output.WriteAsync(new PlainMessage("Username:", OutputSeverity.System, OutputCategory.Notification))
                    .ConfigureAwait(false);

                var username = await ReadLineAsync(ct).ConfigureAwait(false);
                if (username is null) return null;
                username = username.Trim();

                var error = ValidateUsername(username);
                if (error is not null)
                {
                    await output.WriteAsync(new PlainMessage(error, OutputSeverity.Error, OutputCategory.Notification))
                        .ConfigureAwait(false);
                    continue;
                }

                if (mustBeNew && _accountSystem.UsernameExists(username))
                {
                    await output.WriteAsync(new PlainMessage(
                        "That username is already taken.", OutputSeverity.Error, OutputCategory.Notification))
                        .ConfigureAwait(false);
                    continue;
                }

                return username;
            }
        }

        private async Task<string?> PromptPasswordWithConfirmationAsync(
            IOutputWriter output, CancellationToken ct)
        {
            while (true)
            {
                await output.WriteAsync(new PlainMessage("Choose a password:", OutputSeverity.System, OutputCategory.Notification))
                    .ConfigureAwait(false);
                var password = await ReadLineAsync(ct).ConfigureAwait(false);
                if (password is null) return null;

                if (password.Length < 6)
                {
                    await output.WriteAsync(new PlainMessage(
                        "Password must be at least 6 characters.", OutputSeverity.Error, OutputCategory.Notification))
                        .ConfigureAwait(false);
                    continue;
                }

                await output.WriteAsync(new PlainMessage("Confirm password:", OutputSeverity.System, OutputCategory.Notification))
                    .ConfigureAwait(false);
                var confirm = await ReadLineAsync(ct).ConfigureAwait(false);
                if (confirm is null) return null;

                if (password != confirm)
                {
                    await output.WriteAsync(new PlainMessage(
                        "Passwords do not match. Please try again.", OutputSeverity.Error, OutputCategory.Notification))
                        .ConfigureAwait(false);
                    continue;
                }

                return password;
            }
        }

        private async Task<string?> ReadLineAsync(CancellationToken ct)
        {
            try { return await _reader.ReadLineAsync(ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return null; }
            catch (IOException) { return null; }
        }

        // ── Validation ─────────────────────────────────────────────────────────────────

        private string? ValidateCharacterName(string name)
        {
            if (name.Length < 2 || name.Length > 16)
                return "Character name must be between 2 and 16 characters.";
            foreach (var c in name)
                if (!char.IsLetter(c))
                    return "Character name may only contain letters.";
            if (_accountSystem.CharacterNameExists(name))
                return "That character name is already taken.";
            return null;
        }

        private static string? ValidateUsername(string username)
        {
            if (username.Length < 3 || username.Length > 20)
                return "Username must be between 3 and 20 characters.";
            foreach (var c in username)
                if (!char.IsLetterOrDigit(c) && c != '_')
                    return "Username may only contain letters, digits, and underscores.";
            return null;
        }

        private static string BuildRoster(IReadOnlyList<CharacterSummary> characters)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Your characters:");
            for (var i = 0; i < characters.Count; i++)
                sb.AppendLine($"  {i + 1}. {characters[i].CharacterName}");
            sb.Append("Enter a number to play, or 'new' to create a character.");
            return sb.ToString();
        }

        private static bool IsYes(string s)
        {
            var t = s.Trim();
            return t.Equals("yes", StringComparison.OrdinalIgnoreCase)
                || t.Equals("y", StringComparison.OrdinalIgnoreCase)
                || t.Equals("login", StringComparison.OrdinalIgnoreCase);
        }
    }

    internal sealed record LoginResult(uint CharacterEntityId, uint AccountEntityId, string CharacterName);
}
