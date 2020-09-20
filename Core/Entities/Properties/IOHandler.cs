using Hedron.Core.Commands;
using System;
using System.Collections.Generic;
using System.Text;

namespace Hedron.Core.System
{
	/// <summary>
	/// Manages input and output queues for raw text and command queues
	/// </summary>
	public class IOHandler
	{
		private readonly Queue<string> _rawInput;
		private readonly Queue<string> _rawOutput;
		private readonly Queue<KeyValuePair<Command, string>> _cmdInput;

		public IOHandler()
		{
			_rawInput = new Queue<string>();
			_rawOutput = new Queue<string>();
			_cmdInput = new Queue<KeyValuePair<Command, string>>();
		}

		/// <summary>
		/// Queues raw text input
		/// </summary>
		/// <param name="message">The input to queue</param>
		public void QueueRawInput(string message)
		{
			_rawInput.Enqueue(message);
		}

		/// <summary>
		/// Dequeues raw text input
		/// </summary>
		/// <returns>The first queue item</returns>
		public string DequeueRawInput()
		{
			return _rawInput.Dequeue();
		}

		/// <summary>
		/// The count of the current raw input queue
		/// </summary>
		public int RawInputCount
		{
			get
			{
				return _rawInput.Count;
			}
		}

		/// <summary>
		/// Queues raw text output
		/// </summary>
		/// <param name="message">The output to queue</param>
		public void QueueRawOutput(string message)
		{
			_rawOutput.Enqueue(message);
		}

		/// <summary>
		/// Dequeues raw text output
		/// </summary>
		/// <returns>The first queue item</returns>
		public string DequeueRawOutput()
		{
			return _rawOutput.Dequeue();
		}

		/// <summary>
		/// The count of the current raw output queue
		/// </summary>
		public int RawOutputCount
		{
			get
			{
				return _rawOutput.Count;
			}
		}

		/// <summary>
		/// Clears the raw text input queue
		/// </summary>
		public void ClearRawInput()
		{
			_rawInput.Clear();
		}

		/// <summary>
		/// Clear the raw text output queue
		/// </summary>
		public void ClearRawOutput()
		{
			_rawOutput.Clear();
		}

		/// <summary>
		/// Queues a command
		/// </summary>
		/// <param name="command">The command to queue</param>
		/// <param name="args">The command arguments</param>
		public void QueueCommand(Command command, string args)
		{
			_cmdInput.Enqueue(new KeyValuePair<Command, string>(command, args));
		}

		/// <summary>
		/// Dequeues a command
		/// </summary>
		/// <returns>The command and associated argument</returns>
		public KeyValuePair<Command, string> DequeueCommand()
		{
			return _cmdInput.Dequeue();
		}
	}
}