using System;
using System.IO;
using System.Linq;

namespace Dumper {
    public class Program {
        public static void Main() {
            var type = typeof(Dalamud.Interface.FontAwesomeIcon);
            var names = Enum.GetNames(type);
            var matches = names.Where(n => n.Contains("Sword") || n.Contains("Shield") || n.Contains("Dungeon") || n.Contains("Skull") || n.Contains("Dragon") || n.Contains("Crosshairs") || n.Contains("Khanda")).ToList();
            File.WriteAllLines("FA_Matches.txt", matches);
        }
    }
}
