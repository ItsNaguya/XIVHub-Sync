using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using Newtonsoft.Json.Linq;
using XIVHubCompanion.Tablet;

namespace XIVHubCompanion.Apps
{
    /// <summary>
    /// Crafting (3.0): add recipes, the pipeline (materials, gathering, vendors, shopping) and the macro solver.
    /// All data lives in the website's crafting.* state sections, so the tablet and the website show the same thing.
    /// </summary>
    public partial class CraftingApp : IApp
    {
        public string Name => "Crafting";
        public string Icon => ((char)FontAwesomeIcon.Hammer).ToString();
        public bool HasSettings => false;
        public bool LargeText => true;
        public void DrawSettings() { }
        public void Update() { }
        public void Dispose() { }

        private const string SecPipeline = "crafting.pipeline_v2", SecProfiles = "crafting.job_profiles", SecFood = "crafting.global_food",
            SecPotion = "crafting.global_potion", SecView = "crafting.view", SecDraft = "crafting.draft", SecScope = "crafting.priceScope";
        private static readonly string[] Views = { "search", "pipeline", "solver" };
        private static readonly string[] Jobs = { "CRP", "BSM", "ARM", "GSM", "LTW", "WVR", "ALC", "CUL" };
        private static readonly string[] JobNames = { "Carpenter", "Blacksmith", "Armorer", "Goldsmith", "Leatherworker", "Weaver", "Alchemist", "Culinarian" };
        private static readonly uint[] JobIcons = { 62108, 62109, 62110, 62111, 62112, 62113, 62114, 62115 };

        private readonly HubClient _sender;
        private readonly Configuration _configuration;
        private readonly IDataManager _dataManager;
        private readonly IPluginLog _log;
        private readonly ITextureProvider _textureProvider;

        private int _view;
        private bool _viewLoaded;

        public CraftingApp(HubClient sender, Configuration configuration, IDataManager dataManager, IPluginLog log, ITextureProvider textureProvider)
        {
            _sender = sender;
            _configuration = configuration;
            _dataManager = dataManager;
            _log = log;
            _textureProvider = textureProvider;
        }

        public void Draw()
        {
            SyncIn();
            if (!_viewLoaded && _sender.StateLoaded)
            {
                int i = Array.IndexOf(Views, (_sender.GetState(SecView) as JValue)?.Value as string);
                if (i >= 0) _view = i;
                _viewLoaded = true;
            }

            DrawTabs();
            if (_view == 0) DrawSearchView();
            else if (_view == 1) DrawPipelineView();
            else DrawSolverView();
        }

        private void DrawTabs()
        {
            var p = ImGui.GetCursorScreenPos();
            float w = ImGui.GetContentRegionAvail().X, h = S(34), gap = S(8), bw = (w - gap * 2) / 3;
            string[] labels = { _draft.Count > 0 ? $"Add recipes ({_draft.Count})" : "Add recipes", $"Pipeline ({_targets.Count})", "Solver" };
            FontAwesomeIcon[] icons = { FontAwesomeIcon.Search, FontAwesomeIcon.ListUl, FontAwesomeIcon.Magic };
            for (int i = 0; i < 3; i++)
            {
                if (Ui.Button("cf_tab" + i, labels[i], p + new Vector2(i * (bw + gap), 0), new Vector2(bw, h), i == _view ? Kind.Primary : Kind.Ghost, icons[i]) && i != _view)
                {
                    _view = i;
                    _sender.SetState(SecView, Views[i]);
                }
            }
            Advance(p, h + S(6));
        }

        // =============================================================================================================
        // Sync with the website (crafting.* sections). Each section is re-read when HubClient swaps its token.
        // =============================================================================================================
        private JToken? _pipeTok, _draftTok;
        private List<JObject> _targets = new();
        private JObject _items = new();
        private List<JObject> _draft = new();
        private bool _migrated;

        private void SyncIn()
        {
            var tok = _sender.GetState(SecPipeline);
            if (!ReferenceEquals(tok, _pipeTok))
            {
                _pipeTok = tok;
                var o = tok as JObject;
                _targets = (o?["targets"] as JArray)?.OfType<JObject>().Select(t => (JObject)t.DeepClone()).ToList() ?? new();
                _items = (o?["items"] as JObject)?.DeepClone() as JObject ?? new JObject();
                _rollup = null;
            }

            var d = _sender.GetState(SecDraft);
            if (!ReferenceEquals(d, _draftTok))
            {
                _draftTok = d;
                _draft = (d as JArray)?.OfType<JObject>().Select(t => (JObject)t.DeepClone()).ToList() ?? new();
            }

            // Plugin 2.x kept the pipeline in its own config: move it over once if the website has none yet.
            if (!_migrated && _sender.StateLoaded)
            {
                _migrated = true;
                if (tok == null && _targets.Count == 0 && _configuration.CraftingActivePipeline.Count > 0)
                {
                    foreach (var c in _configuration.CraftingActivePipeline) AddTarget(c.RecipeId, c.Amount, write: false);
                    WritePipeline(rebuild: true);
                }
            }
        }

        private void WritePipeline(bool rebuild)
        {
            if (rebuild) RebuildItems();
            _sender.SetState(SecPipeline, new JObject { ["targets"] = new JArray(_targets), ["items"] = _items });
            var now = _sender.GetState(SecPipeline);
            if (now != null) _pipeTok = now;
            _rollup = null;
        }

        private void WriteDraft()
        {
            _sender.SetState(SecDraft, new JArray(_draft));
            var now = _sender.GetState(SecDraft);
            if (now != null) _draftTok = now;
        }

        private static int Int(JToken? t, string key, int def = 0)
        {
            var v = t?[key];
            if (v == null || v.Type == JTokenType.Null) return def;
            try { return (int)Math.Round(v.Value<double>()); } catch { return def; }
        }

        private static string Str(JToken? t, string key) => t?[key]?.Type == JTokenType.String ? t[key]!.ToString() : "";

        private uint TargetRecipe(JObject t)
        {
            var sheet = _dataManager.GetExcelSheet<Recipe>();
            uint rid = (uint)Math.Max(0, Int(t, "recipeId"));
            var r = sheet.GetRowOrDefault(rid);
            if (rid != 0 && r.HasValue && r.Value.ItemResult.RowId == (uint)Int(t, "itemId")) return rid;
            InitRecipeCache();
            return _recipeIdByResult!.TryGetValue((uint)Int(t, "itemId"), out var byItem) ? byItem : rid;
        }

        /// <summary>Adds a target in the website's shape, or raises the amount of the same recipe.</summary>
        private void AddTarget(uint recipeId, int qty, bool write = true)
        {
            var r = _dataManager.GetExcelSheet<Recipe>().GetRowOrDefault(recipeId);
            if (!r.HasValue || r.Value.ItemResult.RowId == 0) return;
            var item = r.Value.ItemResult.Value;
            var same = _targets.FirstOrDefault(t => Int(t, "itemId") == (int)item.RowId);
            if (same != null) same["quantity"] = Int(same, "quantity") + qty;
            else
            {
                var rl = r.Value.RecipeLevelTable.Value;
                _targets.Add(new JObject
                {
                    ["id"] = Guid.NewGuid().ToString("N")[..9],
                    ["recipeId"] = recipeId,
                    ["itemId"] = item.RowId,
                    ["name"] = item.Name.ToString(),
                    ["job"] = JobOf(r.Value),
                    ["level"] = (int)rl.ClassJobLevel,
                    ["stars"] = (int)rl.Stars,
                    ["quantity"] = qty,
                    ["icon"] = IconPath(item.Icon),
                    ["addedAt"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                });
            }
            if (write) WritePipeline(rebuild: true);
        }

        private static string JobOf(Recipe r) => r.CraftType.RowId < 8 ? Jobs[r.CraftType.RowId] : "CRP";
        private static string IconPath(uint icon) => $"/i/{icon / 1000 * 1000:000000}/{icon:000000}.png";

        // =============================================================================================================
        // Small tablet-style widgets used by the three views
        // =============================================================================================================
        private static float S(float v) => Ui.S(v);
        private static Vector2 S(float x, float y) => Ui.S(x, y);
        private static float LH => Ui.TextSize("Ag").Y;
        private static float FrameH => ImGui.GetFontSize() + S(7) * 2;

        /// <summary>Puts the cursor below a block drawn by hand at p (the dummy keeps ImGui's layout honest).</summary>
        private static void Advance(Vector2 p, float h)
        {
            ImGui.SetCursorScreenPos(p);
            ImGui.Dummy(new Vector2(S(1), Math.Max(1, h)));
        }

        private static void PushFrame()
        {
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, S(9, 7));
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, S(6));
            ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
            ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, S(6));
            ImGui.PushStyleColor(ImGuiCol.FrameBg, Theme.Night1.With(0.9f));
            ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, Theme.Night2);
            ImGui.PushStyleColor(ImGuiCol.FrameBgActive, Theme.Night2);
            ImGui.PushStyleColor(ImGuiCol.Border, Theme.CrystalLine);
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Text);
            ImGui.PushStyleColor(ImGuiCol.TextDisabled, Theme.Text3);
            ImGui.PushStyleColor(ImGuiCol.PopupBg, Theme.Night1.With(0.98f));
            ImGui.PushStyleColor(ImGuiCol.Header, Ui.Accent.With(0.22f));
            ImGui.PushStyleColor(ImGuiCol.HeaderHovered, Ui.Accent.With(0.3f));
            ImGui.PushStyleColor(ImGuiCol.SliderGrab, Ui.Accent);
            ImGui.PushStyleColor(ImGuiCol.SliderGrabActive, Theme.Text);
        }

        private static void PopFrame()
        {
            ImGui.PopStyleColor(11);
            ImGui.PopStyleVar(4);
        }

        private static bool Input(string id, ref string value, string hint, float width, Vector2 pos)
        {
            ImGui.SetCursorScreenPos(pos);
            PushFrame();
            ImGui.SetNextItemWidth(width);
            bool changed = ImGui.InputTextWithHint(id, hint, ref value, 100);
            PopFrame();
            return changed;
        }

        private static bool IntInput(string id, ref int value, float width, Vector2 pos, int min = 0, int max = 999999)
        {
            ImGui.SetCursorScreenPos(pos);
            PushFrame();
            ImGui.SetNextItemWidth(width);
            int v = value;
            bool changed = ImGui.InputInt(id, ref v, 0, 0);
            PopFrame();
            v = Math.Clamp(v, min, max);
            if (changed && v != value) { value = v; return true; }
            return false;
        }

        /// <summary>A square check with its label; returns true when toggled.</summary>
        private static bool Check(string id, string label, ref bool value, Vector2 pos)
        {
            float box = S(18);
            var size = new Vector2(box + S(8) + Ui.TextSize(label).X, Math.Max(box, LH));
            bool clicked = Ui.Hit(id, pos, size, out float h);
            var dl = Ui.Dl;
            var b = pos + new Vector2(0, (size.Y - box) / 2);
            dl.AddRectFilled(b, b + new Vector2(box, box), (value ? Ui.Accent.With(0.25f + 0.15f * h) : Theme.Night1.With(0.9f)).U32(), S(4));
            dl.AddRect(b, b + new Vector2(box, box), (value ? Ui.Accent : Theme.Text3.With(0.5f + 0.4f * h)).U32(), S(4), 0, S(1));
            if (value) Ui.IconCentered(dl, b + new Vector2(box, box) / 2, FontAwesomeIcon.Check.ToIconString(), Ui.Accent, 0.7f);
            Ui.Text(dl, pos + new Vector2(box + S(8), (size.Y - LH) / 2), label, UIHelper.LerpColor(Theme.Text2, Theme.Text, h));
            if (clicked) value = !value;
            return clicked;
        }

        /// <summary>A toggle chip that wraps onto the next line; returns true when clicked.</summary>
        private static bool ChipToggle(string id, string text, bool active, ref Vector2 at, float left, float right, float scale = 0.85f)
        {
            var ts = Ui.TextSize(text, scale);
            var size = ts + S(16, 8);
            if (at.X + size.X > right && at.X > left) at = new Vector2(left, at.Y + size.Y + S(6));
            bool clicked = Ui.Hit(id, at, size, out float h);
            var dl = Ui.Dl;
            var col = active ? Ui.Accent : Theme.Text2;
            dl.AddRectFilled(at, at + size, (active ? col.With(0.2f + 0.1f * h) : Theme.Night1.With(0.7f + 0.2f * h)).U32(), size.Y / 2);
            dl.AddRect(at, at + size, col.With(active ? 0.7f : 0.2f + 0.3f * h).U32(), size.Y / 2, 0, S(1));
            Ui.Text(dl, at + S(8, 4), text, active ? Theme.Text : UIHelper.LerpColor(Theme.Text2, Theme.Text, h), scale);
            at.X += size.X + S(6);
            return clicked;
        }

        private static float ChipH(float scale = 0.85f) => Ui.TextSize("Ag", scale).Y + S(8);

        /// <summary>[-] n [+]; returns true when the value changed. Width is StepperW.</summary>
        private static bool Stepper(string id, ref int value, Vector2 pos, int min = 1)
        {
            float b = S(26);
            bool changed = false;
            if (Ui.IconButton(id + "_m", FontAwesomeIcon.Minus, pos, b) && value > min) { value--; changed = true; }
            var txt = value.ToString();
            var ts = Ui.TextSize(txt);
            Ui.Text(Ui.Dl, pos + new Vector2(b + (S(44) - ts.X) / 2, (b - ts.Y) / 2), txt, Theme.Text);
            if (Ui.IconButton(id + "_p", FontAwesomeIcon.Plus, pos + new Vector2(b + S(44), 0), b)) { value++; changed = true; }
            return changed;
        }

        private static float StepperW => S(96);

        private void GameIcon(uint icon, Vector2 pos, float size, float alpha = 1f)
        {
            if (icon == 0) return;
            var wrap = _textureProvider.GetFromGameIcon(new GameIconLookup(icon)).GetWrapOrDefault();
            if (wrap != null) Ui.Dl.AddImage(wrap.Handle, pos, pos + new Vector2(size, size), Vector2.Zero, Vector2.One, new Vector4(1, 1, 1, alpha).U32());
        }

        private static string Fit(string text, float width, float scale = 1f)
        {
            if (Ui.TextSize(text, scale).X <= width) return text;
            while (text.Length > 1 && Ui.TextSize(text + "...", scale).X > width) text = text[..^1];
            return text + "...";
        }
    }
}
