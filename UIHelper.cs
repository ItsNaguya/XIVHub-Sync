using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;

namespace XIVHubCompanion
{
    public static class UIHelper
    {
        private static Dictionary<uint, float> _hoverStates = new Dictionary<uint, float>();
        private static Dictionary<uint, float> _smoothScrollTargets = new Dictionary<uint, float>();
        private static Dictionary<uint, float> _lastFrameScrolls = new Dictionary<uint, float>();
        [ThreadStatic] private static byte[]? _inputBuffer;

        public static bool BeginSmoothChild(string id, Vector2 size = default, bool border = false, ImGuiWindowFlags flags = 0)
        {
            if (PluginUI.HideScrollbars)
            {
                flags |= ImGuiWindowFlags.NoScrollbar;
            }

            bool originallyDisabled = (flags & ImGuiWindowFlags.NoScrollWithMouse) != 0;
            flags |= ImGuiWindowFlags.NoScrollWithMouse;
            
            bool result = ImGui.BeginChild(id, size, border, flags);
            
            uint hashId = ImGui.GetID(id);
            float currentScrollY = ImGui.GetScrollY();
            float maxScrollY = ImGui.GetScrollMaxY();
            
            if (!_smoothScrollTargets.ContainsKey(hashId)) _smoothScrollTargets[hashId] = currentScrollY;
            if (!_lastFrameScrolls.ContainsKey(hashId)) _lastFrameScrolls[hashId] = currentScrollY;
            
            if (Math.Abs(currentScrollY - _lastFrameScrolls[hashId]) > 1.0f)
            {
                _smoothScrollTargets[hashId] = currentScrollY;
            }

            if (!originallyDisabled && ImGui.IsWindowHovered() && ImGui.GetIO().MouseWheel != 0)
            {
                _smoothScrollTargets[hashId] = _smoothScrollTargets[hashId] - ImGui.GetIO().MouseWheel * 120.0f;
            }
            
            _smoothScrollTargets[hashId] = Math.Clamp(_smoothScrollTargets[hashId], 0, maxScrollY);
            
            float dt = ImGui.GetIO().DeltaTime;
            if (Math.Abs(currentScrollY - _smoothScrollTargets[hashId]) > 0.5f)
            {
                float nextScroll = Lerp(currentScrollY, _smoothScrollTargets[hashId], dt * 15.0f);
                ImGui.SetScrollY(nextScroll);
                _lastFrameScrolls[hashId] = nextScroll;
            }
            else
            {
                _lastFrameScrolls[hashId] = currentScrollY;
            }

            return result;
        }

        public static float GetHoverState(uint hash, bool isHovered, float speed = 5.0f)
        {
            if (!_hoverStates.ContainsKey(hash)) _hoverStates[hash] = 0f;
            
            float dt = ImGui.GetIO().DeltaTime;
            if (isHovered)
                _hoverStates[hash] = Math.Min(1.0f, _hoverStates[hash] + dt * speed);
            else
                _hoverStates[hash] = Math.Max(0.0f, _hoverStates[hash] - dt * speed);
                
            return _hoverStates[hash];
        }

        public static float PeekHoverState(uint hash)
        {
            if (_hoverStates.ContainsKey(hash))
                return _hoverStates[hash];
            return 0f;
        }

        public static Vector4 LerpColor(Vector4 a, Vector4 b, float t)
        {
            return new Vector4(
                a.X + (b.X - a.X) * t,
                a.Y + (b.Y - a.Y) * t,
                a.Z + (b.Z - a.Z) * t,
                a.W + (b.W - a.W) * t
            );
        }

        public static uint Vec4ToU32(Vector4 color)
        {
            return ImGui.ColorConvertFloat4ToU32(color);
        }

        public static void DrawCard(Vector2 pos, Vector2 size, Vector4 bgColor, float rounding, Vector4 borderColor)
        {
            float actualRounding = Math.Max(rounding, 8f * PluginUI.AppScale); // Enforce premium rounding
            var drawList = ImGui.GetWindowDrawList();
            
            // Subtle glassmorphism background adjustment
            Vector4 glassBg = new Vector4(bgColor.X, bgColor.Y, bgColor.Z, Math.Max(0.5f, bgColor.W * 0.8f));
            drawList.AddRectFilled(pos, pos + size, Vec4ToU32(glassBg), actualRounding);
            
            // Subtle top highlight for 3D glass effect
            drawList.AddRectFilled(pos, pos + new Vector2(size.X, 2f), Vec4ToU32(new Vector4(1f, 1f, 1f, 0.05f)), actualRounding, ImDrawFlags.RoundCornersTop);
            
            // Premium border
            Vector4 premiumBorder = new Vector4(borderColor.X, borderColor.Y, borderColor.Z, Math.Max(0.2f, borderColor.W));
            drawList.AddRect(pos, pos + size, Vec4ToU32(premiumBorder), actualRounding, 0, 1.5f);
        }

        public static bool DrawPremiumButton(string id, Vector2 pos, Vector2 size, string text, Vector4 baseBg, Vector4 hoverBg, Vector4 baseText, Vector4 hoverText, bool centerText = true)
        {
            ImGui.SetCursorScreenPos(pos);
            ImGui.InvisibleButton(id, size);
            bool isHovered = ImGui.IsItemHovered();
            bool isActive = ImGui.IsItemActive();
            bool isClicked = ImGui.IsItemClicked();

            uint baseId = ImGui.GetID(id);
            float hoverState = GetHoverState(baseId, isHovered, 12.0f);
            float activeState = GetHoverState(baseId ^ 0x12345678, isActive, 20.0f);

            var currentBg = LerpColor(baseBg, hoverBg, hoverState);
            var drawList = ImGui.GetWindowDrawList();
            
            float rounding = 8f;
            drawList.AddRectFilled(pos, pos + size, Vec4ToU32(currentBg), rounding);
            
            if (hoverState > 0.01f)
            {
                Vector4 glowColor = LerpColor(new Vector4(0.0f, 0.65f, 1.0f, 0f), new Vector4(0.0f, 0.65f, 1.0f, 0.8f), hoverState);
                drawList.AddRect(pos, pos + size, Vec4ToU32(glowColor), rounding, 0, 1.5f);
            }
            else 
            {
                drawList.AddRect(pos, pos + size, Vec4ToU32(new Vector4(0.3f, 0.3f, 0.35f, 0.5f)), rounding, 0, 1.0f);
            }

            drawList.PushClipRect(pos, pos + size, true);
            var currentText = LerpColor(baseText, hoverText, hoverState);
            var textSize = ImGui.CalcTextSize(text);
            
            Vector2 textOffset = new Vector2(0, activeState * 1.5f);
            
            Vector2 textPos;
            if (centerText)
            {
                textPos = pos + new Vector2(size.X / 2 - textSize.X / 2, size.Y / 2 - textSize.Y / 2) + textOffset;
            }
            else
            {
                textPos = pos + new Vector2(8, size.Y / 2 - textSize.Y / 2) + textOffset; 
            }
            
            drawList.AddText(textPos, Vec4ToU32(currentText), text);
            drawList.PopClipRect();

            return isClicked;
        }

        public static bool DrawPremiumCalculateButton(string id, Vector2 pos, Vector2 size, string text, bool isCalculating, Dalamud.Bindings.ImGui.ImTextureID? textureHandle = null)
        {
            float buttonWidth = Math.Min(400f, size.X);
            Vector2 buttonSize = new Vector2(buttonWidth, size.Y);
            Vector2 buttonPos = new Vector2(pos.X + (size.X - buttonWidth) / 2, pos.Y);

            ImGui.SetCursorScreenPos(buttonPos);
            bool isHovered = false;
            bool isClicked = false;
            
            if (isCalculating)
            {
                ImGui.InvisibleButton(id, buttonSize);
            }
            else
            {
                isClicked = ImGui.InvisibleButton(id, buttonSize);
                isHovered = ImGui.IsItemHovered();
            }

            uint baseId = ImGui.GetID(id);
            float hoverState = GetHoverState(baseId, isHovered, 10.0f);
            var drawList = ImGui.GetWindowDrawList();
            
            Vector4 baseBg = new Vector4(0.08f, 0.08f, 0.1f, 1.0f);
            Vector4 hoverBg = new Vector4(0.12f, 0.12f, 0.15f, 1.0f);
            Vector4 currentBg = LerpColor(baseBg, hoverBg, hoverState);
            
            uint ceruleumBlue = Vec4ToU32(new Vector4(0.0f, 0.65f, 1.0f, 1.0f));
            uint edgeHighlight = Vec4ToU32(new Vector4(0.3f, 0.3f, 0.35f, 0.5f));

            if (isCalculating)
                currentBg = new Vector4(0.05f, 0.05f, 0.06f, 1.0f);

            float rounding = 12f;
            drawList.AddRectFilled(buttonPos, buttonPos + buttonSize, Vec4ToU32(currentBg), rounding);
            
            uint borderColor = isHovered && !isCalculating ? ceruleumBlue : edgeHighlight;
            drawList.AddRect(buttonPos, buttonPos + buttonSize, borderColor, rounding, 0, isHovered && !isCalculating ? 2f : 1.0f);

            if (isCalculating)
            {
                float time = (float)ImGui.GetTime();
                float pulse = (float)Math.Sin(time * 6.0f) * 0.3f + 0.5f;
                drawList.AddRect(buttonPos, buttonPos + buttonSize, Vec4ToU32(new Vector4(0.0f, 0.65f, 1.0f, pulse)), rounding, 0, 2f);
                
                float scanX = (time * buttonSize.X * 1.5f) % (buttonSize.X * 2) - buttonSize.X;
                float startX = Math.Max(buttonPos.X, buttonPos.X + scanX);
                float endX = Math.Min(buttonPos.X + buttonSize.X, buttonPos.X + scanX + 60f);
                
                if (endX > startX)
                {
                    drawList.AddRectFilledMultiColor(
                        new Vector2(startX, buttonPos.Y + 2), 
                        new Vector2(endX, buttonPos.Y + buttonSize.Y - 2),
                        Vec4ToU32(new Vector4(0f, 0.65f, 1f, 0f)),
                        Vec4ToU32(new Vector4(0f, 0.65f, 1f, 0.3f)),
                        Vec4ToU32(new Vector4(0f, 0.65f, 1f, 0.3f)),
                        Vec4ToU32(new Vector4(0f, 0.65f, 1f, 0f))
                    );
                }
            }

            var textSize = ImGui.CalcTextSize(text);
            Vector2 textPos = buttonPos + new Vector2((buttonSize.X - textSize.X) / 2, (buttonSize.Y - textSize.Y) / 2);

            ImGui.SetCursorScreenPos(textPos);
            ImGui.TextColored(new Vector4(1, 1, 1, 0.9f), text);

            return isClicked;
        }

        public static bool DrawPremiumWarningButton(string id, Vector2 pos, Vector2 size, string text)
        {
            ImGui.SetCursorScreenPos(pos);
            bool isClicked = ImGui.InvisibleButton(id, size);
            bool isHovered = ImGui.IsItemHovered();

            uint baseId = ImGui.GetID(id);
            float hoverState = GetHoverState(baseId, isHovered, 10.0f);
            
            var drawList = ImGui.GetWindowDrawList();
            
            Vector4 baseBg = new Vector4(0.12f, 0.12f, 0.14f, 1.0f); // Gunmetal
            Vector4 hoverBg = new Vector4(0.2f, 0.2f, 0.22f, 1.0f);
            Vector4 currentBg = LerpColor(baseBg, hoverBg, hoverState);
            
            float rounding = 8f;
            drawList.AddRectFilled(pos, pos + size, Vec4ToU32(currentBg), rounding);
            
            if (hoverState > 0.01f)
            {
                drawList.AddRect(pos, pos + size, Vec4ToU32(new Vector4(1.0f, 0.5f, 0.2f, hoverState)), rounding, 0, 1.5f);
            }

            var currentText = new Vector4(1f, 1f, 1f, 1f);
            var textSize = ImGui.CalcTextSize(text);
            Vector2 textPos = pos + new Vector2(size.X / 2 - textSize.X / 2, size.Y / 2 - textSize.Y / 2);
            drawList.AddText(textPos, Vec4ToU32(currentText), text);

            return isClicked;
        }

        public static bool DrawPremiumCheckbox(string id, Vector2 pos, ref bool isChecked)
        {
            float size = 18f;
            ImGui.SetCursorScreenPos(pos);
            ImGui.InvisibleButton(id, new Vector2(size, size));
            bool isHovered = ImGui.IsItemHovered();
            bool isClicked = ImGui.IsItemClicked();
            if (isClicked) isChecked = !isChecked;

            uint baseId = ImGui.GetID(id);
            float hoverState = GetHoverState(baseId, isHovered, 10.0f);
            float checkState = GetHoverState(baseId ^ 0x12345678, isChecked, 15.0f);

            var drawList = ImGui.GetWindowDrawList();
            Vector4 darkIron = new Vector4(0.12f, 0.12f, 0.14f, 1.0f);
            Vector4 borderCol = LerpColor(new Vector4(0.3f, 0.3f, 0.35f, 1.0f), new Vector4(0.0f, 0.65f, 1.0f, 1.0f), hoverState * 0.5f + checkState * 0.5f);
            
            float rounding = 4f;
            drawList.AddRectFilled(pos, pos + new Vector2(size, size), Vec4ToU32(darkIron), rounding);
            drawList.AddRect(pos, pos + new Vector2(size, size), Vec4ToU32(borderCol), rounding, 0, 1.5f);

            if (checkState > 0.01f)
            {
                float innerSize = size * 0.5f * checkState;
                Vector2 center = pos + new Vector2(size / 2, size / 2);
                drawList.AddRectFilled(center - new Vector2(innerSize / 2), center + new Vector2(innerSize / 2), Vec4ToU32(new Vector4(0.0f, 0.65f, 1.0f, checkState)), rounding * 0.5f);
            }

            return isClicked;
        }

        public static bool DrawPremiumCheckboxWithText(string id, string text, ref bool isChecked)
        {
            Vector2 startPos = ImGui.GetCursorPos();
            bool result = DrawPremiumCheckbox(id, ImGui.GetCursorScreenPos(), ref isChecked);
            ImGui.SameLine(0, 10);
            ImGui.SetCursorPosY(startPos.Y + 2);
            ImGui.Text(text);
            ImGui.Dummy(new Vector2(0, 4));
            return result;
        }
        
        public static bool DrawPremiumRadioButton(string id, Vector2 pos, ref int currentVal, int targetVal)
        {
            float radius = 9f;
            ImGui.SetCursorScreenPos(pos);
            ImGui.InvisibleButton(id, new Vector2(radius * 2, radius * 2));
            bool isHovered = ImGui.IsItemHovered();
            bool isSelected = (currentVal == targetVal);
            bool isClicked = ImGui.IsItemClicked();
            if (isClicked) currentVal = targetVal;

            uint baseId = ImGui.GetID(id);
            float hoverState = GetHoverState(baseId, isHovered, 10.0f);
            float selectState = GetHoverState(baseId ^ 0x12345678, isSelected, 15.0f);

            var drawList = ImGui.GetWindowDrawList();
            Vector2 center = pos + new Vector2(radius, radius);
            
            drawList.AddCircleFilled(center, radius, Vec4ToU32(new Vector4(0.12f, 0.12f, 0.14f, 1.0f)));
            Vector4 borderCol = LerpColor(new Vector4(0.3f, 0.3f, 0.35f, 1.0f), new Vector4(0.0f, 0.65f, 1.0f, 1.0f), hoverState * 0.5f + selectState * 0.5f);
            drawList.AddCircle(center, radius, Vec4ToU32(borderCol), 0, 1.5f);

            if (selectState > 0.01f)
            {
                drawList.AddCircleFilled(center, radius * 0.5f * selectState, Vec4ToU32(new Vector4(0.0f, 0.65f, 1.0f, selectState)));
            }
            
            return isClicked;
        }

        public static bool DrawPremiumRadioButtonWithText(string id, string text, ref int currentVal, int targetVal)
        {
            Vector2 startPos = ImGui.GetCursorPos();
            bool result = DrawPremiumRadioButton(id, ImGui.GetCursorScreenPos(), ref currentVal, targetVal);
            ImGui.SameLine(0, 10);
            ImGui.SetCursorPosY(startPos.Y + 1);
            ImGui.Text(text);
            return result;
        }

        public static bool DrawPremiumInputText(string id, Vector2 pos, Vector2 size, ref string text, uint maxLength)
        {
            var drawList = ImGui.GetWindowDrawList();
            ImGui.SetCursorScreenPos(pos);
            
            bool isHovered = ImGui.IsMouseHoveringRect(pos, pos + size);
            bool isActive = ImGui.IsItemActive(); 

            uint baseId = ImGui.GetID(id);
            float hoverState = GetHoverState(baseId, isHovered, 10.0f);
            float activeState = GetHoverState(baseId ^ 0x12345678, isActive, 10.0f);

            Vector4 bgCol = new Vector4(0.08f, 0.08f, 0.09f, 1.0f);
            Vector4 borderCol = LerpColor(new Vector4(0.3f, 0.3f, 0.35f, 0.5f), new Vector4(0.0f, 0.65f, 1.0f, 1.0f), hoverState * 0.5f + activeState * 0.5f);
            
            float rounding = 8f;
            drawList.AddRectFilled(pos, pos + size, Vec4ToU32(bgCol), rounding);
            drawList.AddRect(pos, pos + size, Vec4ToU32(borderCol), rounding, 0, activeState > 0.01f ? 1.5f : 1.0f);

            if (activeState > 0.01f)
            {
                drawList.AddRect(pos - new Vector2(1,1), pos + size + new Vector2(1,1), Vec4ToU32(new Vector4(0.0f, 0.65f, 1.0f, activeState * 0.3f)), rounding, 0, 2f);
            }

            ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0,0,0,0));
            ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0,0,0,0));
            ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new Vector4(0,0,0,0));
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.9f, 0.9f, 0.9f, 1.0f));
            
            float padY = Math.Max(0, (size.Y - ImGui.GetTextLineHeight()) * 0.5f);
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(12f, padY));
            ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 0f);
            
            ImGui.SetNextItemWidth(size.X);
            
            if (_inputBuffer == null) _inputBuffer = new byte[8192];
            Array.Clear(_inputBuffer, 0, _inputBuffer.Length);
            
            if (!string.IsNullOrEmpty(text))
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(text);
                Array.Copy(bytes, _inputBuffer, Math.Min(bytes.Length, _inputBuffer.Length - 1));
            }
            
            string inputId = id.StartsWith("##") ? id : $"##{id}";
            bool changed = ImGui.InputText(inputId, _inputBuffer);
            if (changed)
            {
                int nullIdx = Array.IndexOf(_inputBuffer, (byte)0);
                text = System.Text.Encoding.UTF8.GetString(_inputBuffer, 0, nullIdx >= 0 ? nullIdx : _inputBuffer.Length);
            }
            
            isActive = ImGui.IsItemActive();
            GetHoverState(ImGui.GetID(id) ^ 0x12345678, isActive, 10.0f); 

            ImGui.PopStyleVar(2);
            ImGui.PopStyleColor(4);
            
            return changed;
        }
        public static bool DrawPremiumTabSegment(string[] tabs, ref int activeIndex, float totalWidth)
        {
            bool changed = false;
            float spacing = 8f * PluginUI.AppScale;
            float segmentWidth = (totalWidth - (spacing * (tabs.Length - 1))) / tabs.Length;
            Vector2 btnSize = new Vector2(segmentWidth, 35f * PluginUI.AppScale);
            
            Vector4 bgActive = new Vector4(0.2f, 0.4f, 0.8f, 1f); // Ceruleum Blue
            Vector4 bgNormal = new Vector4(0.12f, 0.12f, 0.14f, 1f);
            Vector4 textNormal = new Vector4(0.8f, 0.8f, 0.8f, 1f);
            Vector4 textActive = new Vector4(1f, 1f, 1f, 1f);

            for (int i = 0; i < tabs.Length; i++)
            {
                if (i > 0) ImGui.SameLine(0, spacing);
                
                if (DrawPremiumButton("tab_" + tabs[i], ImGui.GetCursorScreenPos(), btnSize, tabs[i], 
                    activeIndex == i ? bgActive : bgNormal, 
                    bgActive, 
                    activeIndex == i ? textActive : textNormal, 
                    textActive))
                {
                    activeIndex = i;
                    changed = true;
                }
            }
            return changed;
        }


        public static bool DrawPremiumCollapsingHeader(string id, string text, ref bool isOpen)
        {
            var pos = ImGui.GetCursorScreenPos();
            var size = new Vector2(ImGui.GetContentRegionAvail().X, 30);
            
            bool isHovered = ImGui.IsMouseHoveringRect(pos, pos + size);
            bool isClicked = isHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left);
            
            if (isClicked) isOpen = !isOpen;

            uint baseId = ImGui.GetID(id);
            float hoverState = GetHoverState(baseId, isHovered, 10.0f);
            
            var drawList = ImGui.GetWindowDrawList();
            Vector4 bgCol = new Vector4(0.12f, 0.12f, 0.14f, 1.0f);
            Vector4 hoverBg = new Vector4(0.18f, 0.18f, 0.22f, 1.0f);
            var currentBg = LerpColor(bgCol, hoverBg, hoverState);
            
            float rounding = 8f;
            drawList.AddRectFilled(pos, pos + size, Vec4ToU32(currentBg), rounding);
            drawList.AddRect(pos, pos + size, Vec4ToU32(new Vector4(0.3f, 0.3f, 0.35f, 0.5f)), rounding, 0, 1f);

            float arrowSize = 10f;
            Vector2 arrowCenter = pos + new Vector2(15, size.Y / 2);
            if (isOpen)
            {
                drawList.AddTriangleFilled(
                    arrowCenter + new Vector2(-arrowSize/2, -arrowSize/4),
                    arrowCenter + new Vector2(arrowSize/2, -arrowSize/4),
                    arrowCenter + new Vector2(0, arrowSize/2),
                    Vec4ToU32(new Vector4(0.9f, 0.9f, 0.9f, 1.0f))
                );
            }
            else
            {
                drawList.AddTriangleFilled(
                    arrowCenter + new Vector2(-arrowSize/4, -arrowSize/2),
                    arrowCenter + new Vector2(-arrowSize/4, arrowSize/2),
                    arrowCenter + new Vector2(arrowSize/2, 0),
                    Vec4ToU32(new Vector4(0.9f, 0.9f, 0.9f, 1.0f))
                );
            }

            ImGui.SetCursorScreenPos(pos + new Vector2(30, (size.Y - ImGui.GetTextLineHeight()) / 2));
            ImGui.Text(text);

            ImGui.SetCursorScreenPos(pos + new Vector2(0, size.Y + 4));
            
            return isOpen;
        }

        private static float Lerp(float a, float b, float t)
        {
            return a + (b - a) * t;
        }

        public static bool DrawPremiumSwitch(string id, Vector2 pos, ref bool isChecked)
        {
            float width = 36f;
            float height = 20f;
            ImGui.SetCursorScreenPos(pos);
            ImGui.InvisibleButton(id, new Vector2(width, height));
            bool isHovered = ImGui.IsItemHovered();
            bool isClicked = ImGui.IsItemClicked();
            if (isClicked) isChecked = !isChecked;

            uint baseId = ImGui.GetID(id);
            float hoverState = GetHoverState(baseId, isHovered, 10.0f);
            float checkState = GetHoverState(baseId ^ 0x12345678, isChecked, 15.0f);

            var drawList = ImGui.GetWindowDrawList();
            Vector4 offBg = new Vector4(0.12f, 0.12f, 0.14f, 1.0f);
            Vector4 onBg = new Vector4(0.0f, 0.5f, 0.9f, 1.0f);
            Vector4 currentBg = LerpColor(offBg, onBg, checkState);
            
            drawList.AddRectFilled(pos, pos + new Vector2(width, height), Vec4ToU32(currentBg), height * 0.5f);
            Vector4 borderCol = LerpColor(new Vector4(0.3f, 0.3f, 0.35f, 0.5f), new Vector4(0.0f, 0.65f, 1.0f, 1.0f), hoverState * 0.5f + checkState * 0.5f);
            drawList.AddRect(pos, pos + new Vector2(width, height), Vec4ToU32(borderCol), height * 0.5f, 0, 1.5f);

            float handleRadius = height * 0.5f - 3f;
            float handleX = Lerp(pos.X + height * 0.5f, pos.X + width - height * 0.5f, checkState);
            Vector2 handleCenter = new Vector2(handleX, pos.Y + height * 0.5f);
            
            drawList.AddCircleFilled(handleCenter, handleRadius + 1f, Vec4ToU32(new Vector4(0.05f, 0.05f, 0.05f, 0.5f))); 
            drawList.AddCircleFilled(handleCenter, handleRadius, Vec4ToU32(new Vector4(0.9f, 0.9f, 0.9f, 1.0f)));
            
            if (checkState > 0.01f) {
                drawList.AddCircleFilled(handleCenter, handleRadius * 0.5f, Vec4ToU32(new Vector4(0.0f, 0.65f, 1.0f, checkState)));
            }
            
            return isClicked;
        }

        public static bool DrawPremiumSwitchWithText(string id, string text, ref bool isChecked)
        {
            Vector2 startPos = ImGui.GetCursorPos();
            bool result = DrawPremiumSwitch(id, ImGui.GetCursorScreenPos(), ref isChecked);
            ImGui.SameLine(0, 10);
            ImGui.SetCursorPosY(startPos.Y + 2);
            ImGui.Text(text);
            ImGui.Dummy(new Vector2(0, 4));
            return result;
        }

        public static void BeginTooltip()
        {
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10, 10));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 12f);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1.0f);
            ImGui.PushStyleColor(ImGuiCol.PopupBg, new Vector4(0.06f, 0.06f, 0.09f, 0.98f));
            ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.3f, 0.3f, 0.35f, 0.5f));
            
            ImGui.BeginTooltip();
        }

        public static void EndTooltip()
        {
            ImGui.EndTooltip();
            ImGui.PopStyleColor(2);
            ImGui.PopStyleVar(3);
        }

        public static void DrawTooltip(string text)
        {
            if (ImGui.IsItemHovered())
            {
                BeginTooltip();
                ImGui.TextUnformatted(text);
                EndTooltip();
            }
        }

        public static void DrawScrollFade(Vector2 min, Vector2 max, Vector4 bgColor, float fadeHeight)
        {
            var drawList = ImGui.GetWindowDrawList();
            
            Vector4 neonGlow = new Vector4(0.0f, 0.65f, 1.0f, 0.5f);
            Vector4 darkShadow = new Vector4(0.0f, 0.0f, 0.0f, 0.6f);
            Vector4 transparent = new Vector4(0.0f, 0.0f, 0.0f, 0.0f);
            
            drawList.AddRectFilledMultiColor(
                new Vector2(min.X, max.Y - fadeHeight),
                max,
                Vec4ToU32(transparent),
                Vec4ToU32(transparent),
                Vec4ToU32(darkShadow),
                Vec4ToU32(darkShadow)
            );
            
            drawList.AddLine(new Vector2(min.X, max.Y), new Vector2(max.X, max.Y), Vec4ToU32(neonGlow), 2f);
        }

        public static bool BeginPremiumModal(string id, ref bool isOpen, Vector2 contentPos, Vector2 contentSize, Vector2 modalSize, out float alpha)
        {
            uint hash = ImGui.GetID(id);
            alpha = GetHoverState(hash, isOpen, 8.0f);
            
            if (alpha <= 0.01f) return false;
            
            ImGui.SetCursorScreenPos(contentPos);
            ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0, 0, 0, 0.75f * alpha));
            UIHelper.BeginSmoothChild(id + "_Overlay", contentSize, false, ImGuiWindowFlags.NoScrollbar);
            
            var drawList = ImGui.GetWindowDrawList();
            
            modalSize.X = Math.Min(modalSize.X, contentSize.X - 20f * PluginUI.AppScale);
            modalSize.Y = Math.Min(modalSize.Y, contentSize.Y - 20f * PluginUI.AppScale);
            
            float scaleY = 0.95f + (0.05f * alpha);
            float currentHeight = modalSize.Y * scaleY;
            Vector2 currentSize = new Vector2(modalSize.X, currentHeight);
            
            Vector2 modalPos = contentPos + (contentSize - currentSize) * 0.5f;
            
            Vector4 bgColor = new Vector4(0.04f, 0.05f, 0.08f, 0.98f * alpha);
            drawList.AddRectFilled(modalPos, modalPos + currentSize, ImGui.ColorConvertFloat4ToU32(bgColor), 12f);
            drawList.AddRect(modalPos, modalPos + currentSize, ImGui.ColorConvertFloat4ToU32(new Vector4(0.3f, 0.3f, 0.35f, 0.3f * alpha)), 12f, 0, 1f);
            
            ImGui.SetCursorScreenPos(modalPos + new Vector2(20, 20) * PluginUI.AppScale);
            ImGui.PushStyleVar(ImGuiStyleVar.Alpha, alpha);
            
            UIHelper.BeginSmoothChild(id + "_Content", currentSize - new Vector2(40, 40) * PluginUI.AppScale, false, ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoScrollbar);
            
            return true;
        }

        public static void EndPremiumModal()
        {
            ImGui.EndChild(); // Content
            ImGui.PopStyleVar();
            ImGui.EndChild(); // Overlay
            ImGui.PopStyleColor();
        }
    }
}
