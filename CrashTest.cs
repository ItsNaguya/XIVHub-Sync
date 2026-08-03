using System;
using System.Linq;

namespace XIVHubCompanion.Apps
{
    public static class CrashTest
    {
        public static void Run(Lumina.Excel.ExcelSheet<Lumina.Excel.Sheets.Recipe> sheet)
        {
            var recipe = sheet.GetRow(1);
            var ing = recipe.Ingredient[0].Value;
            int count = recipe.Ingredient.Count;
        }
    }
}
