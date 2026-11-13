using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Lumina.Excel.Sheets;
using Newtonsoft.Json.Linq;
using XIVHubCompanion.Tablet;

namespace XIVHubCompanion.Apps
{
    public partial class CraftingApp
    {
        private bool _solverLoaded, _ensureReliability, _editProfile;
        private int _editJob = -1;
        private volatile bool _isSolving;
        private string? _solveError;
        private SolveResult? _solveResult;
        private uint _solveRecipe;

        private uint _ctxRecipe;
        private int _maxProgress = 6600, _maxQuality = 14000, _maxDurability = 70, _rlvl = 640, _job;
        private bool _expert;
        private string _ctxName = "";

        // HQ materials -> starting quality (the game's rule: share of HQ item levels times the recipe's material quality factor)
        private bool _qualityMatters = true;   // false: the result can't be HQ and isn't a collectable
        private int _maxIq;                    // quality * MaterialQualityFactor / 100
        private readonly List<(uint Id, string Name, uint Icon, int Amount, int Ilvl)> _hqIngs = new();
        private readonly Dictionary<uint, int> _hqCounts = new();

        private class SolveResult
        {
            public int step, progress, quality, cp, durability;
            public List<string> actions = new();
        }

        // ---- consumables: the website's list (data/consumables.ts), shared through crafting.global_food/potion ----
        private static JObject Cons(int id, string name, bool hq, int icon, int ilvl, params (string Stat, int V, int Max)[] stats)
        {
            var s = new JObject();
            foreach (var (stat, v, max) in stats) s[stat] = new JObject { ["value"] = v, ["max"] = max };
            return new JObject { ["id"] = id, ["name"] = name, ["hq"] = hq, ["icon"] = $"https://xivapi.com/i/046000/{icon:000000}.png", ["itemLevel"] = ilvl, ["stats"] = s };
        }

        private static readonly JObject[] Meals =
        {
            Cons(44158, "Rroneek Steak", true, 46201, 690, ("CP", 26, 92), ("Control", 5, 97)), Cons(44158, "Rroneek Steak", false, 46201, 690, ("CP", 21, 74), ("Control", 4, 78)),
            Cons(44159, "Calamari Ripieni", true, 46202, 690, ("CP", 26, 82), ("Craftsmanship", 5, 120)), Cons(44159, "Calamari Ripieni", false, 46202, 690, ("CP", 21, 66), ("Craftsmanship", 4, 96)),
            Cons(44160, "Pineapple Salad", true, 46203, 690, ("CP", 13, 41), ("Control", 6, 135)), Cons(44160, "Pineapple Salad", false, 46203, 690, ("CP", 10, 33), ("Control", 5, 108)),
            Cons(40669, "All i Pebre", true, 46205, 640, ("CP", 26, 100), ("Control", 5, 115)), Cons(40669, "All i Pebre", false, 46205, 640, ("CP", 21, 80), ("Control", 4, 92)),
            Cons(40668, "Ceviche", true, 46206, 640, ("CP", 26, 96), ("Craftsmanship", 5, 150)), Cons(40668, "Ceviche", false, 46206, 640, ("CP", 21, 76), ("Craftsmanship", 4, 120)),
            Cons(38260, "Jhinga Biryani", true, 46182, 620, ("CP", 26, 86), ("Control", 5, 90)), Cons(38260, "Jhinga Biryani", false, 46182, 620, ("CP", 21, 69), ("Control", 4, 72)),
            Cons(36053, "Tsai tou Vounou", true, 46175, 560, ("CP", 26, 78), ("Control", 5, 76)), Cons(36053, "Tsai tou Vounou", false, 46175, 560, ("CP", 21, 62), ("Control", 4, 61)),
            Cons(27835, "Blood Bouillabaisse", true, 46145, 430, ("CP", 26, 66), ("Control", 5, 122)), Cons(27835, "Blood Bouillabaisse", false, 46145, 430, ("CP", 21, 53), ("Control", 4, 98)),
            Cons(12854, "Seafood Stew", true, 46059, 150, ("CP", 15, 50), ("Control", 5, 31)), Cons(12854, "Seafood Stew", false, 46059, 150, ("CP", 12, 40), ("Control", 4, 25)),
        };

        private static readonly JObject[] Potions =
        {
            Cons(44161, "Cunning Craftsman's Tisane", true, 46210, 690, ("CP", 6, 27)), Cons(44161, "Cunning Craftsman's Tisane", false, 46210, 690, ("CP", 5, 21)),
            Cons(44162, "Competent Craftsman's Tisane", true, 46211, 690, ("Craftsmanship", 3, 63)), Cons(44162, "Competent Craftsman's Tisane", false, 46211, 690, ("Craftsmanship", 2, 50)),
            Cons(44163, "Commanding Craftsman's Tisane", true, 46212, 690, ("Control", 3, 63)), Cons(44163, "Commanding Craftsman's Tisane", false, 46212, 690, ("Control", 2, 50)),
            Cons(37752, "Cunning Craftsman's Draught", true, 46180, 580, ("CP", 6, 21)), Cons(37752, "Cunning Craftsman's Draught", false, 46180, 580, ("CP", 5, 16)),
            Cons(37750, "Competent Craftsman's Draught", true, 46178, 580, ("Craftsmanship", 3, 50)), Cons(37750, "Competent Craftsman's Draught", false, 46178, 580, ("Craftsmanship", 2, 40)),
            Cons(37751, "Commanding Craftsman's Draught", true, 46179, 580, ("Control", 3, 50)), Cons(37751, "Commanding Craftsman's Draught", false, 46179, 580, ("Control", 2, 40)),
        };

        private static string CKey(JToken? c) => c is JObject o ? $"{Int(o, "id")}-{(o["hq"]?.Type == JTokenType.Boolean && (bool)o["hq"]! ? "hq" : "nq")}" : "";
        private static string CLabel(JToken? c) => c is JObject o
            ? $"{Str(o, "name")} {(CKey(o).EndsWith("hq") ? "(HQ)" : "(NQ)")}"
            : "None";
        private static string CStats(JToken? c) => c is JObject o && o["stats"] is JObject s
            ? string.Join(", ", s.Properties().Select(p => $"{p.Name} +{Int(p.Value, "value")}% (max {Int(p.Value, "max")})")) : "";

        // ---- crafter profiles: crafting.job_profiles (the live stats from the game land there too) ----------------
        private JObject Profile(int job)
        {
            if ((_sender.GetState(SecProfiles) as JObject)?[Jobs[job]] is JObject e) return e;
            var cfg = _configuration.CrafterProfiles.Count > job ? _configuration.CrafterProfiles[job] : null;
            return new JObject
            {
                ["level"] = cfg?.Level ?? 100, ["craftsmanship"] = cfg?.Craftsmanship > 0 ? cfg.Craftsmanship : 4800, ["control"] = cfg?.Control > 0 ? cfg.Control : 4700,
                ["cp"] = cfg?.Cp > 0 ? cfg.Cp : 600, ["specialist"] = false, ["manipulation"] = true, ["heartAndSoul"] = false, ["quickInnovation"] = false,
            };
        }

        private static bool Flag(JObject p, string key, bool def) => p[key]?.Type == JTokenType.Boolean ? (bool)p[key]! : def;

        private void SetProfile(int job, string key, JToken value, bool allJobs = false)
        {
            var all = (_sender.GetState(SecProfiles) as JObject)?.DeepClone() as JObject ?? new JObject();
            for (int j = 0; j < 8; j++)
            {
                if (!allJobs && j != job) continue;
                var e = all[Jobs[j]] as JObject ?? (JObject)Profile(j).DeepClone();
                e[key] = value.DeepClone();
                all[Jobs[j]] = e;
                if (_configuration.CrafterProfiles.Count > j && value.Type == JTokenType.Integer)
                {
                    var c = _configuration.CrafterProfiles[j];
                    int v = (int)value;
                    if (key == "level") c.Level = v; else if (key == "craftsmanship") c.Craftsmanship = v; else if (key == "control") c.Control = v; else if (key == "cp") c.Cp = v;
                }
            }
            _sender.SetState(SecProfiles, all);
            _configuration.Save();
        }

        private (int Craft, int Control, int Cp) Buffed(JObject p)
        {
            int craft = Int(p, "craftsmanship"), control = Int(p, "control"), cp = Int(p, "cp");
            int bc = craft, bo = control, bp = cp;
            foreach (var c in new[] { _sender.GetState(SecFood), _sender.GetState(SecPotion) })
            {
                if ((c as JObject)?["stats"] is not JObject s) continue;
                if (s["Craftsmanship"] is JObject a) bc += Math.Min(Int(a, "max"), (int)Math.Floor(craft * Int(a, "value") / 100.0));
                if (s["Control"] is JObject b) bo += Math.Min(Int(b, "max"), (int)Math.Floor(control * Int(b, "value") / 100.0));
                if (s["CP"] is JObject d) bp += Math.Min(Int(d, "max"), (int)Math.Floor(cp * Int(d, "value") / 100.0));
            }
            return (bc, bo, bp);
        }

        private void UpdateRecipeContext()
        {
            if (_configuration.SolverSelectedTarget < 0 || _configuration.SolverSelectedTarget >= _targets.Count) { _ctxRecipe = 0; return; }
            var t = _targets[_configuration.SolverSelectedTarget];
            uint rid = TargetRecipe(t);
            if (rid == _ctxRecipe) return;
            var r = _dataManager.GetExcelSheet<Recipe>().GetRowOrDefault(rid);
            if (!r.HasValue) { _ctxRecipe = 0; return; }
            _ctxRecipe = rid;
            _ctxName = Str(t, "name");
            var rl = r.Value.RecipeLevelTable.Value;
            _rlvl = (int)r.Value.RecipeLevelTable.RowId;
            _maxProgress = (int)Math.Floor(rl.Difficulty * r.Value.DifficultyFactor / 100f);
            _maxQuality = (int)Math.Floor(rl.Quality * r.Value.QualityFactor / 100f);
            _maxDurability = (int)Math.Floor(rl.Durability * r.Value.DurabilityFactor / 100f);
            _expert = r.Value.IsExpert;
            _job = r.Value.CraftType.RowId < 8 ? (int)r.Value.CraftType.RowId : 0;
            if (_solveRecipe != rid) { _solveResult = null; _solveError = null; }

            var result = r.Value.ItemResult.Value;
            _qualityMatters = r.Value.CanHq || result.IsCollectable;
            _maxIq = _qualityMatters ? (int)Math.Floor(_maxQuality * r.Value.MaterialQualityFactor / 100.0) : 0;
            _hqIngs.Clear();
            _hqCounts.Clear();
            var items = _dataManager.GetExcelSheet<Item>();
            for (int i = 0; i < r.Value.Ingredient.Count; i++)
            {
                uint id = r.Value.Ingredient[i].RowId;
                int amt = r.Value.AmountIngredient[i];
                var it = items.GetRowOrDefault(id);
                // only materials that exist in HQ count (crystals and NQ-only items never do)
                if (id == 0 || amt <= 0 || !it.HasValue || !it.Value.CanBeHq || it.Value.ItemUICategory.RowId == 59) continue;
                _hqIngs.Add((id, it.Value.Name.ToString(), it.Value.Icon, amt, (int)Math.Max(1, it.Value.LevelItem.RowId)));
            }
            _configuration.SolverInitialQuality = Math.Clamp(_configuration.SolverInitialQuality, 0, _maxIq);
        }

        // =============================================================================================================
        private void DrawSolverView()
        {
            var dl = Ui.Dl;
            var top = ImGui.GetCursorScreenPos();
            var avail = ImGui.GetContentRegionAvail();
            if (!_solverLoaded)
            {
                _solverLoaded = true;
                if (_configuration.SolverSelectedTarget < 0 && _targets.Count > 0) _configuration.SolverSelectedTarget = 0;
            }
            if (_targets.Count == 0)
            {
                Ui.Panel(dl, top, top + new Vector2(avail.X, S(110)), Theme.Night2.With(0.6f));
                Ui.Text(dl, top + S(18, 18), "No recipes in your pipeline yet.", Theme.Text);
                Ui.Text(dl, top + new Vector2(S(18), S(24) + LH), "The solver makes a macro for any recipe in your pipeline.", Theme.Text3, 0.9f);
                Advance(top, S(110));
                return;
            }
            if (_configuration.SolverSelectedTarget >= _targets.Count) _configuration.SolverSelectedTarget = 0;
            UpdateRecipeContext();

            // ---- recipe / food / potion --------------------------------------------------------------------------
            float gap = S(10), rw = avail.X * 0.4f, fw = (avail.X - rw - gap * 2) / 2;
            Ui.Caps(dl, top, "Recipe");
            Ui.Caps(dl, top + new Vector2(rw + gap, 0), "Food");
            Ui.Caps(dl, top + new Vector2(rw + fw + gap * 2, 0), "Potion");
            float cy = top.Y + LH + S(4);
            PushFrame();
            ImGui.SetCursorScreenPos(new Vector2(top.X, cy));
            ImGui.SetNextItemWidth(rw);
            if (ImGui.BeginCombo("##cf_srec", _ctxName.Length > 0 ? _ctxName : "Pick a recipe"))
            {
                for (int i = 0; i < _targets.Count; i++)
                    if (ImGui.Selectable($"{Str(_targets[i], "name")} ({Str(_targets[i], "job")} Lv {Int(_targets[i], "level")})##t{i}", i == _configuration.SolverSelectedTarget))
                    {
                        _configuration.SolverSelectedTarget = i;
                        _configuration.Save();
                        UpdateRecipeContext();
                    }
                ImGui.EndCombo();
            }
            ConsumableCombo("##cf_sfood", SecFood, Meals, new Vector2(top.X + rw + gap, cy), fw);
            ConsumableCombo("##cf_spot", SecPotion, Potions, new Vector2(top.X + rw + fw + gap * 2, cy), fw);
            PopFrame();
            float y = cy + FrameH + S(12);

            bool wide = avail.X >= S(760);
            float colW = wide ? (avail.X - gap) * 0.45f : avail.X;
            ImGui.SetCursorScreenPos(new Vector2(top.X, y));
            if (UIHelper.BeginSmoothChild("cf_solver", new Vector2(avail.X, top.Y + avail.Y - y)))
            {
                var cp = ImGui.GetCursorScreenPos();
                float leftBottom = DrawSolverSetup(cp, colW - S(10));
                if (wide)
                {
                    float rightBottom = DrawSolverResult(cp + new Vector2(colW + gap, 0), avail.X - colW - gap - S(10));
                    Advance(cp, Math.Max(leftBottom, rightBottom) - cp.Y + S(4));
                }
                else
                {
                    float rb = DrawSolverResult(new Vector2(cp.X, leftBottom + S(10)), colW - S(10));
                    Advance(cp, rb - cp.Y + S(4));
                }
            }
            ImGui.EndChild();
        }

        private void ConsumableCombo(string id, string section, JObject[] list, Vector2 pos, float width)
        {
            var cur = _sender.GetState(section);
            ImGui.SetCursorScreenPos(pos);
            ImGui.SetNextItemWidth(width);
            if (ImGui.BeginCombo(id, CLabel(cur)))
            {
                if (ImGui.Selectable("None", cur is not JObject)) _sender.SetState(section, null);
                foreach (var c in list)
                    if (ImGui.Selectable($"{CLabel(c)}  ·  {CStats(c)}##{CKey(c)}", CKey(c) == CKey(cur))) _sender.SetState(section, c.DeepClone());
                ImGui.EndCombo();
            }
            if (cur is JObject && ImGui.IsItemHovered()) UIHelper.DrawTooltip(CStats(cur));
        }

        /// <summary>Crafter stats, profile editor, starting quality and options; returns the bottom edge.</summary>
        private float DrawSolverSetup(Vector2 p, float w)
        {
            var dl = Ui.Dl;
            dl.ChannelsSplit(2);
            dl.ChannelsSetCurrent(1);
            float pad = S(14), x = p.X + pad, iw = w - pad * 2, y = p.Y + pad;
            int job = _editJob >= 0 ? _editJob : _job;
            var prof = Profile(job);
            var (bc, bo, bp) = Buffed(prof);

            GameIcon(JobIcons[job], new Vector2(x, y), S(30));
            Ui.Text(dl, new Vector2(x + S(40), y), JobNames[job], Theme.Gold);
            Ui.Text(dl, new Vector2(x + S(40), y + LH), $"Level {Int(prof, "level")}", Theme.Text3, 0.85f);
            if (Ui.IconButton("cf_pedit", FontAwesomeIcon.Edit, new Vector2(x + iw - S(28), y), S(28), _editProfile ? "Close the editor" : "Edit crafter stats", null, _editProfile))
            {
                _editProfile = !_editProfile;
                _editJob = -1;
            }
            y += Math.Max(S(30), LH * 2) + S(10);

            if (_editProfile)
            {
                var at = new Vector2(x, y);
                for (int j = 0; j < 8; j++)
                    if (ChipToggle("cf_pj" + j, Jobs[j], j == job, ref at, x, x + iw, 0.8f)) _editJob = j == _job ? -1 : j;
                y = at.Y + ChipH(0.8f) + S(10);
                float fw2 = (iw - S(10)) / 2;
                void Field(string label, string key, float fx, float fy, int max)
                {
                    Ui.Text(dl, new Vector2(fx, fy), label, Theme.Text3, 0.8f);
                    int v = Int(prof, key);
                    if (IntInput("##cf_pf" + key, ref v, fw2, new Vector2(fx, fy + LH * 0.8f + S(2)), 1, max)) SetProfile(job, key, v);
                }
                Field("Level", "level", x, y, 100);
                Field("CP", "cp", x + fw2 + S(10), y, 9999);
                y += LH * 0.8f + FrameH + S(10);
                Field("Craftsmanship", "craftsmanship", x, y, 99999);
                Field("Control", "control", x + fw2 + S(10), y, 99999);
                y += LH * 0.8f + FrameH + S(10);
                if (Ui.Button("cf_pcopy", "Copy to all crafters", new Vector2(x, y), new Vector2(iw, S(28)), Kind.Ghost, FontAwesomeIcon.Copy))
                    foreach (var k in new[] { "level", "craftsmanship", "control", "cp" }) SetProfile(job, k, Int(prof, k), allJobs: true);
                y += S(28) + S(8);
                Ui.Text(dl, new Vector2(x, y), Fit("Stats update from your crafter in game (without food).", iw, 0.8f), Theme.Text3, 0.8f);
                y += LH + S(10);
            }
            else
            {
                void Row(string label, int baseV, int buffV)
                {
                    Ui.Text(dl, new Vector2(x, y), label, Theme.Text2);
                    var v = buffV.ToString("N0");
                    var vs = Ui.TextSize(v);
                    Ui.Text(dl, new Vector2(x + iw - vs.X, y), v, buffV > baseV ? Theme.Live : Theme.Text);
                    if (buffV > baseV)
                    {
                        var bs = $"{baseV:N0} +{buffV - baseV}";
                        Ui.Text(dl, new Vector2(x + iw - vs.X - Ui.TextSize(bs, 0.8f).X - S(10), y + S(2)), bs, Theme.Text3, 0.8f);
                    }
                    y += LH + S(6);
                }
                Row("Craftsmanship", Int(prof, "craftsmanship"), bc);
                Row("Control", Int(prof, "control"), bo);
                Row("CP", Int(prof, "cp"), bp);
                y += S(6);
            }

            // ---- recipe ---------------------------------------------------------------------------------------------
            Ui.Caps(dl, new Vector2(x, y), "Recipe");
            y += LH + S(2);
            var info = $"Progress {_maxProgress:N0} · Quality {_maxQuality:N0} · Durability {_maxDurability} · rlvl {_rlvl}{(_expert ? " · Expert" : "")}";
            Ui.Text(dl, new Vector2(x, y), Fit(info, iw, 0.85f), Theme.Text2, 0.85f);
            y += LH + S(12);

            // ---- starting quality --------------------------------------------------------------------------------
            Ui.Caps(dl, new Vector2(x, y), "Starting quality");
            y += LH + S(4);
            if (!_qualityMatters)
            {
                Ui.Text(dl, new Vector2(x, y), Fit("This item has no HQ version: quality doesn't matter.", iw, 0.85f), Theme.Text3, 0.85f);
                y += LH + S(12);
            }
            else if (_maxIq <= 0 || _hqIngs.Count == 0)
            {
                Ui.Text(dl, new Vector2(x, y), Fit("No material of this recipe exists in HQ.", iw, 0.85f), Theme.Text3, 0.85f);
                y += LH + S(12);
            }
            else
            {
                // HQ materials: how many of each you put in (the website's QoL list)
                Ui.Text(dl, new Vector2(x + iw - Ui.TextSize("HQ materials", 0.8f).X, y - LH - S(4) + S(2)), "HQ materials", Theme.Text3, 0.8f);
                float rowH = Math.Max(S(28), LH) + S(4), ic = S(24);
                bool countsChanged = false;
                foreach (var ing in _hqIngs)
                {
                    GameIcon(ing.Icon, new Vector2(x, y + (rowH - ic) / 2), ic);
                    Ui.Text(dl, new Vector2(x + ic + S(8), y + (rowH - LH) / 2), Fit($"{ing.Name} x{ing.Amount}", iw - ic - S(16) - StepperW, 0.9f), Theme.Text, 0.9f);
                    int c = _hqCounts.GetValueOrDefault(ing.Id);
                    if (Stepper("cf_hq" + ing.Id, ref c, new Vector2(x + iw - StepperW, y + (rowH - S(26)) / 2), 0))
                    {
                        _hqCounts[ing.Id] = Math.Clamp(c, 0, ing.Amount);
                        countsChanged = true;
                    }
                    y += rowH;
                }
                if (countsChanged)
                {
                    long total = _hqIngs.Sum(i => (long)i.Amount * i.Ilvl);
                    long hq = _hqIngs.Sum(i => (long)_hqCounts.GetValueOrDefault(i.Id) * i.Ilvl);
                    _configuration.SolverInitialQuality = total > 0 ? (int)(_maxIq * hq / total) : 0;
                    _configuration.Save();
                }
                Ui.Text(dl, new Vector2(x, y + S(2)), Fit("HQ count per material, or set the quality directly:", iw, 0.8f), Theme.Text3, 0.8f);
                y += LH * 0.8f + S(8);

                int iq = Math.Clamp(_configuration.SolverInitialQuality, 0, _maxIq);
                float inW = S(84);
                ImGui.SetCursorScreenPos(new Vector2(x, y));
                PushFrame();
                ImGui.SetNextItemWidth(iw - inW - S(8));
                if (ImGui.SliderInt("##cf_iqs", ref iq, 0, _maxIq, "")) { _configuration.SolverInitialQuality = iq; _hqCounts.Clear(); }
                if (ImGui.IsItemDeactivatedAfterEdit()) _configuration.Save();
                PopFrame();
                if (IntInput("##cf_iqi", ref iq, inW, new Vector2(x + iw - inW, y), 0, _maxIq)) { _configuration.SolverInitialQuality = iq; _hqCounts.Clear(); _configuration.Save(); }
                y += FrameH + S(4);
                var mx = $"of {_maxIq:N0} max";
                Ui.Text(dl, new Vector2(x + iw - Ui.TextSize(mx, 0.8f).X, y), mx, Theme.Text3, 0.8f);
                y += LH * 0.8f + S(10);
            }

            // ---- options: per crafter on the website (job_profiles), solver settings here ----------------------------
            Ui.Caps(dl, new Vector2(x, y), "Solver options");
            y += LH + S(6);
            void Opt(string id, string label, string key, bool def)
            {
                bool v = Flag(prof, key, def);
                if (Check("cf_o" + key, label, ref v, new Vector2(x, y))) SetProfile(job, key, v);
                y += Math.Max(S(18), LH) + S(8);
            }
            Opt("m", "Manipulation", "manipulation", true);
            Opt("h", "Heart and Soul", "heartAndSoul", false);
            Opt("q", "Quick Innovation", "quickInnovation", false);
            if (_qualityMatters)
            {
                bool req = _configuration.SolverRequireTargetQuality;
                if (Check("cf_oreq", "Must reach full quality", ref req, new Vector2(x, y))) { _configuration.SolverRequireTargetQuality = req; _configuration.Save(); }
                y += Math.Max(S(18), LH) + S(8);
            }
            Check("cf_orel", "Safe from bad luck (slower)", ref _ensureReliability, new Vector2(x, y));
            y += Math.Max(S(18), LH) + S(14);

            // ---- solve --------------------------------------------------------------------------------------------
            float sh = S(40);
            if (_isSolving)
            {
                var sp = new Vector2(x, y);
                Ui.Panel(dl, sp, sp + new Vector2(iw, sh), Theme.Gold.With(0.1f), Theme.GoldLine, 6f);
                Ui.Dot(dl, sp + new Vector2(S(18), sh / 2), 4f, Theme.Gold, pulse: true);
                Ui.Text(dl, sp + new Vector2(S(32), (sh - LH) / 2), Fit("Solving on this PC...", iw - S(40)), Theme.Gold);
            }
            else if (Ui.Button("cf_solve", "Solve macro", new Vector2(x, y), new Vector2(iw, sh), Kind.Primary, FontAwesomeIcon.Magic) && _ctxRecipe != 0)
                RunSolver(prof, bc, bo, bp);
            y += sh + S(8);
            if (_solveError != null)
            {
                foreach (var line in Wrap(_solveError, iw, 0.85f)) { Ui.Text(dl, new Vector2(x, y), line, Theme.Bad, 0.85f); y += LH * 0.9f + S(2); }
                y += S(4);
            }

            float bottom = y + pad - S(8);
            dl.ChannelsSetCurrent(0);
            Ui.Panel(dl, p, new Vector2(p.X + w, bottom), Theme.Night2.With(0.6f));
            dl.ChannelsMerge();
            return bottom;
        }

        /// <summary>Bars and the macros; returns the bottom edge.</summary>
        private float DrawSolverResult(Vector2 p, float w)
        {
            var dl = Ui.Dl;
            dl.ChannelsSplit(2);
            dl.ChannelsSetCurrent(1);
            float pad = S(14), x = p.X + pad, iw = w - pad * 2, y = p.Y + pad;
            var r = _solveRecipe == _ctxRecipe ? _solveResult : null;
            Ui.Caps(dl, new Vector2(x, y), "Result");
            if (r != null)
            {
                var ok = $"{r.step} steps";
                Ui.Text(dl, new Vector2(x + iw - Ui.TextSize(ok, 0.85f).X, y), ok, Theme.Live, 0.85f);
            }
            y += LH + S(8);

            var (_, _, bp) = Buffed(Profile(_job));
            int iq = _configuration.SolverInitialQuality;
            void Bar(string label, int cur, int max, Vector4 col)
            {
                Ui.Text(dl, new Vector2(x, y), label, Theme.Text3, 0.82f);
                var v = $"{cur:N0} / {max:N0}";
                Ui.Text(dl, new Vector2(x + iw - Ui.TextSize(v, 0.82f).X, y), v, Theme.Text2, 0.82f);
                y += LH * 0.82f + S(4);
                Ui.Progress(dl, new Vector2(x, y), iw, max > 0 ? (float)cur / max : 0, col, 6f);
                y += S(14);
            }
            Bar("Progress", r?.progress ?? 0, _maxProgress, Theme.Crystal);
            if (_qualityMatters) Bar("Quality", Math.Min(_maxQuality, (r?.quality ?? 0) + iq), _maxQuality, Theme.Violet);
            Bar("Durability", r != null ? Math.Max(0, r.durability) : _maxDurability, _maxDurability, Theme.Warn);
            Bar("CP", r != null ? Math.Max(0, r.cp) : bp, bp, Theme.Gold);
            y += S(6);

            var blocks = MacroBlocks(r);
            if (blocks.Count == 0)
            {
                Ui.Text(dl, new Vector2(x, y), "Solve to get your macro.", Theme.Text3);
                y += LH + S(6);
            }
            for (int i = 0; i < blocks.Count; i++)
            {
                Ui.Caps(dl, new Vector2(x, y + S(4)), blocks.Count > 1 ? $"Macro {i + 1} of {blocks.Count}" : "Macro", Theme.Gold);
                if (Ui.Button("cf_copy" + i, "Copy", new Vector2(x + iw - S(96), y), new Vector2(S(96), S(28)), Kind.Ghost, FontAwesomeIcon.Copy))
                    ImGui.SetClipboardText(blocks[i]);
                y += S(34);
                var lines = blocks[i].Split('\n');
                float bh = lines.Length * (LH * 0.85f + S(2)) + S(16);
                var bp0 = new Vector2(x, y);
                dl.AddRectFilled(bp0, bp0 + new Vector2(iw, bh), Theme.Night0.With(0.6f).U32(), S(6));
                float ly = y + S(8);
                foreach (var l in lines) { Ui.Text(dl, new Vector2(x + S(10), ly), Fit(l, iw - S(20), 0.85f), Theme.Live.With(0.9f), 0.85f); ly += LH * 0.85f + S(2); }
                y += bh + S(10);
            }

            float bottom = y + pad - S(8);
            dl.ChannelsSetCurrent(0);
            Ui.Panel(dl, p, new Vector2(p.X + w, bottom), Theme.Night2.With(0.6f));
            dl.ChannelsMerge();
            return bottom;
        }

        private static List<string> Wrap(string text, float width, float scale)
        {
            var lines = new List<string>();
            var cur = "";
            foreach (var word in text.Split(' '))
            {
                var next = cur.Length == 0 ? word : cur + " " + word;
                if (Ui.TextSize(next, scale).X > width && cur.Length > 0) { lines.Add(cur); cur = word; }
                else cur = next;
            }
            if (cur.Length > 0) lines.Add(cur);
            return lines;
        }

        /// <summary>In-game macros hold 15 lines: 14 actions and an echo per block.</summary>
        private static List<string> MacroBlocks(SolveResult? r)
        {
            var blocks = new List<string>();
            if (r == null || r.actions.Count == 0) return blocks;
            var lines = r.actions.Select(a => $"/ac \"{a}\" <wait.3>").ToList();
            if (lines.Count <= 15)
            {
                blocks.Add(string.Join("\n", lines) + (lines.Count < 15 ? "\n/echo Craft finished! <se.8>" : ""));
                return blocks;
            }
            for (int i = 0; i < lines.Count; i += 14)
            {
                var b = lines.Skip(i).Take(14).ToList();
                b.Add(i + 14 >= lines.Count ? "/echo Craft finished! <se.8>" : $"/echo Macro #{i / 14 + 1} finished! <se.8>");
                blocks.Add(string.Join("\n", b));
            }
            return blocks;
        }

        private async void RunSolver(JObject prof, int craft, int control, int cp)
        {
            if (_isSolving) return;
            _isSolving = true;
            _solveError = null;
            uint rid = _ctxRecipe;
            var body = new
            {
                stats = new
                {
                    level = Int(prof, "level", 100), craftsmanship = craft, control, cp,
                    manipulation = Flag(prof, "manipulation", true), heartAndSoul = Flag(prof, "heartAndSoul", false), quickInnovation = Flag(prof, "quickInnovation", false),
                },
                // no HQ version: quality 0 makes the solver go for the shortest progress-only macro
                recipe = new { progress = _maxProgress, quality = _qualityMatters ? _maxQuality : 0, durability = _maxDurability, rlvl = _rlvl, isExpert = _expert },
                hqInitialQuality = _qualityMatters ? Math.Clamp(_configuration.SolverInitialQuality, 0, _maxIq) : 0,
                settings = new { requireTargetQuality = _qualityMatters && _configuration.SolverRequireTargetQuality, ensureReliability = _ensureReliability },
            };
            try
            {
                // Only on this PC (aetherhold_solver.dll next to the plugin); the plugin never uses the server solver.
                var dir = System.IO.Path.GetDirectoryName(typeof(LocalSolver).Assembly.Location) ?? "";
                if (!LocalSolver.Available(dir))
                {
                    _log.Warning($"Local solver unavailable: {LocalSolver.LoadError}");
                    _solveError = $"The solver file could not be loaded ({LocalSolver.LoadError}). Your antivirus may have blocked aetherhold_solver.dll; reinstalling the plugin brings it back.";
                    return;
                }
                string? json = await LocalSolver.SolveAsync(Newtonsoft.Json.JsonConvert.SerializeObject(body));
                JObject? j = null;
                try { if (json != null) j = JObject.Parse(json); } catch { }
                var actions = (j?["actions"] as JArray)?.Select(a => a.ToString()).ToList();
                if (actions != null && actions.Count > 0)
                {
                    _solveResult = new SolveResult
                    {
                        step = Int(j, "step", actions.Count), progress = Int(j, "progress"), quality = Int(j, "quality"), cp = Int(j, "cp"), durability = Int(j, "durability"), actions = actions,
                    };
                    _solveRecipe = rid;
                }
                else _solveError = Str(j, "error") is { Length: > 0 } e
                    ? (e.Contains("NoSolution") ? "The solver could not find a macro for these stats. Try other settings." : e)
                    : "The solver gave no answer. Try again.";
            }
            catch (Exception ex) { _solveError = "The solver failed: " + ex.Message; }
            finally { _isSolving = false; }
        }
    }
}
