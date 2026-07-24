# XIV Hub Companion

The official Dalamud companion plugin for [XIV Hub](https://xiv.naguya.tech). 
Because clicking "Sync" is so 2023.

This plugin seamlessly and automatically syncs your character data (stats, inventory, retainers, and gear) locally in real-time while you play FFXIV.

## Installation Instructions

Currently, this plugin is in early access/development. To install it using Dalamud's Dev Tools:

1. **Download the latest release**: Grab the `XIVHubCompanion.dll` from the latest release, or clone this repository and compile it yourself using `dotnet build -c Release`.
2. **Open Dalamud Settings in-game**: Type `/xlsettings` in the FFXIV chat.
3. **Enable Developer Mode**: Go to the **Experimental** tab and check **Enable Developer Mode**.
4. **Open the Dev Tools**: Type `/xlplugins` and click on **Dev Tools** in the bottom left corner.
5. **Install the Plugin**: 
   - Click on **Installed Dev Plugins**.
   - Click the folder icon with a `+` (or type the path) to point it to the folder containing your `XIVHubCompanion.dll` and `XIVHubCompanion.json`.
6. **Enable it**: Ensure the plugin is turned on (the toggle switch is green).
7. **Configure**: Type `/xivhub` in-game to open the settings menu and make sure syncing is enabled!

## Contributing

Feel free to submit pull requests if you want to improve the sync logic, add more features, or optimize memory reading.
