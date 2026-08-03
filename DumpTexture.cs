using System;
using System.Reflection;
using Dalamud.Interface.Textures.TextureWraps;

namespace XIVHubCompanion
{
    public class DumpTexture
    {
        public void Dump()
        {
            Type t = typeof(IDalamudTextureWrap);
            foreach (var prop in t.GetProperties())
            {
                Console.WriteLine("Prop: " + prop.Name);
            }
            foreach (var meth in t.GetMethods())
            {
                Console.WriteLine("Meth: " + meth.Name);
            }
        }
    }
}
