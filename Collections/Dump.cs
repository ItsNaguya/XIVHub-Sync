using System;
using System.IO;
using Lumina.Excel.Sheets;

namespace XIVHubCompanion
{
    public static class DumpLumina
    {
        public static void Dump()
        {
            var p1 = typeof(Orchestrion).GetProperties();
            using var sw = new StreamWriter("lumina_dump.txt");
            sw.WriteLine("Orchestrion:");
            foreach(var p in p1) sw.WriteLine(p.Name);
            
            var p2 = typeof(TripleTriadCard).GetProperties();
            sw.WriteLine("TripleTriadCard:");
            foreach(var p in p2) sw.WriteLine(p.Name);
        }
    }
}
