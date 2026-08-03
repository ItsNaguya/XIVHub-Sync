const fs = require('fs');
const path = require('path');
const icons = ['Collection.png', 'CoreSettings.png', 'Crafting.png', 'Events.png', 'Gathering.png', 'Market.png', 'RaidPlanner.png', 'Routines.png'];

let csharp = 'namespace XIVHubCompanion {\n    public static class IconData {\n';
for (const icon of icons) {
  const p = path.join(__dirname, 'images', icon);
  const base64 = fs.readFileSync(p).toString('base64');
  csharp += '        public static readonly string ' + icon.replace('.png', '') + ' = "' + base64 + '";\n';
}
csharp += '    }\n}\n';
fs.writeFileSync('IconData.cs', csharp);
