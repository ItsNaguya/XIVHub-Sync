const fs = require('fs');
const path = require('path');
const sharp = require('sharp');
const lucide = require('lucide-static');

const icons = [
  { name: 'Market', icon: 'Coins' },
  { name: 'Gathering', icon: 'Leaf' },
  { name: 'Crafting', icon: 'Hammer' },
  { name: 'Collection', icon: 'Gem' },
  { name: 'Events', icon: 'CalendarDays' },
  { name: 'Routines', icon: 'ClipboardCheck' },
  { name: 'Raid Planner', icon: 'Swords' },
  { name: 'Core Settings', icon: 'Settings' }
];

const outDir = path.join(__dirname, 'images');
if (!fs.existsSync(outDir)) fs.mkdirSync(outDir);

async function generate() {
  for (const {name, icon} of icons) {
    let svg = lucide[icon];
    if (!svg) { console.log('not found: ' + icon); continue; }
    
    // Convert lucide svg string to 128x128 white stroke
    svg = svg.replace('currentColor', '#FFFFFF')
             .replace('width="24"', 'width="128"')
             .replace('height="24"', 'height="128"');
             
    await sharp(Buffer.from(svg))
      .png()
      .toFile(path.join(outDir, name.replace(' ', '') + '.png'));
    console.log('Generated ' + name);
  }
}
generate();
