# Hedron
A C# mud targeting asp.net core

The solution targets .net core 3.1. There are 2 components to the engine - the web admin UI for editing game content, and a separate thread for running the game loop. The threads share a data cache so updates made in the live editor will be designed to take effect appropriately in the live game. Data will be stored in json format flat files.

If you're interested in helping, discussion of develyor the game in general, or just want to chat, please join us on Discord at https://discord.gg/BafNmpK
