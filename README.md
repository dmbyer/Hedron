# Hedron
A C# mud targeting asp.net core

The solution targets .net core 2.1. There are 2 components to the engine - the web admin UI for editing game content, and a separate thread for running the game loop. The threads share a data cache so updates made in the live editor will be designed to take effect appropriately in the live game. Data will be stored in json format flat files.
