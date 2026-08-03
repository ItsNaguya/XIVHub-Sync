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
            
            // Force disable native mouse scrolling for this child so we can handle it manually
            flags |= ImGuiWindowFlags.NoScrollWithMouse;
            
            bool result = ImGui.BeginChild(id, size, border, flags);
            
            uint hashId = ImGui.GetID(id);
            float currentScrollY = ImGui.GetScrollY();
            float maxScrollY = ImGui.GetScrollMaxY();
            
            if (!_smoothScrollTargets.ContainsKey(hashId)) _smoothScrollTargets[hashId] = currentScrollY;
            if (!_lastFrameScrolls.ContainsKey(hashId)) _lastFrameScrolls[hashId] = currentScrollY;
            
            // If actual scroll differs from what we set last frame, user dragged scrollbar (or ImGui resized/clamped)
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
            var drawList = ImGui.GetWindowDrawList();
            drawList.AddRectFilled(pos, pos + size, Vec4ToU32(bgColor), rounding);
            drawList.AddRect(pos, pos + size, Vec4ToU32(borderColor), rounding, 0, 1.5f);
        }

        public static bool DrawGarlondButton(string id, Vector2 pos, Vector2 size, string text, Vector4 baseBg, Vector4 hoverBg, Vector4 baseText, Vector4 hoverText, bool centerText = true)
        {
            ImGui.SetCursorScreenPos(pos);
            ImGui.InvisibleButton(id, size);
            bool isHovered = ImGui.IsItemHovered();
            bool isActive = ImGui.IsItemActive();
            bool isClicked = ImGui.IsItemClicked();

            uint baseId = ImGui.GetID(id);
            float hoverState = GetHoverState(baseId, isHovered, 10.0f);
            float activeState = GetHoverState(baseId ^ 0x12345678, isActive, 20.0f);

            float pressOffset = activeState * 1.5f;
            Vector2 btnStart = pos + new Vector2(pressOffset, 0);
            Vector2 btnEnd = pos + size + new Vector2(pressOffset, 0);

            var currentBg = LerpColor(baseBg, hoverBg, hoverState);
            var drawList = ImGui.GetWindowDrawList();
            
            drawList.AddRectFilled(btnStart, btnEnd, Vec4ToU32(currentBg), 2f);
            
            // Outer subtle border
            drawList.AddRect(btnStart, btnEnd, Vec4ToU32(new Vector4(0.3f, 0.3f, 0.35f, 1.0f)), 2f, 0, 1f);

            // Top highlight for physical bevel
            drawList.AddLine(btnStart + new Vector2(1, 1), new Vector2(btnEnd.X - 1, btnStart.Y + 1), Vec4ToU32(new Vector4(1, 1, 1, 0.15f + hoverState * 0.2f)), 1f);

            // Sci-fi accent line on the left (Garlond style)
            if (hoverState > 0.01f)
            {
                Vector4 accentColor = LerpColor(new Vector4(0.0f, 0.65f, 1.0f, 0f), new Vector4(0.0f, 0.65f, 1.0f, 1.0f), hoverState);
                drawList.AddRectFilled(btnStart + new Vector2(0, size.Y * 0.2f), btnStart + new Vector2(3, size.Y * 0.8f), Vec4ToU32(accentColor), 1f);
            }

            drawList.PushClipRect(btnStart, btnEnd, true);
            var currentText = LerpColor(baseText, hoverText, hoverState);
            var textSize = ImGui.CalcTextSize(text);
            
            Vector2 textPos;
            if (centerText)
            {
                textPos = btnStart + new Vector2(size.X / 2 - textSize.X / 2, size.Y / 2 - textSize.Y / 2);
            }
            else
            {
                textPos = btnStart + new Vector2(5, size.Y / 2 - textSize.Y / 2); // left align with a small margin
            }
            
            drawList.AddText(textPos, Vec4ToU32(currentText), text);
            drawList.PopClipRect();

            return isClicked;
        }

        public static bool DrawGarlondCalculateButton(string id, Vector2 pos, Vector2 size, string text, bool isCalculating, Dalamud.Bindings.ImGui.ImTextureID? textureHandle = null)
        {
            float buttonWidth = Math.Min(400f, size.X);
            Vector2 buttonSize = new Vector2(buttonWidth, size.Y);
            Vector2 buttonPos = new Vector2(pos.X + (size.X - buttonWidth) / 2, pos.Y);

            ImGui.SetCursorScreenPos(buttonPos);
            bool isHovered = false;
            bool isClicked = false;
            
            if (isCalculating)
            {
                ImGui.InvisibleButton(id, buttonSize); // disabled hit test
            }
            else
            {
                isClicked = ImGui.InvisibleButton(id, buttonSize);
                isHovered = ImGui.IsItemHovered();
            }

            uint baseId = ImGui.GetID(id);
            float hoverState = GetHoverState(baseId, isHovered, 10.0f);
            var drawList = ImGui.GetWindowDrawList();
            
            // Garlond Ironworks Button Colors
            Vector4 baseBg = new Vector4(0.08f, 0.08f, 0.1f, 1.0f);
            Vector4 hoverBg = new Vector4(0.12f, 0.12f, 0.15f, 1.0f);
            Vector4 currentBg = LerpColor(baseBg, hoverBg, hoverState);
            
            uint ceruleumBlue = Vec4ToU32(new Vector4(0.0f, 0.65f, 1.0f, 1.0f));
            uint edgeHighlight = Vec4ToU32(new Vector4(0.3f, 0.3f, 0.35f, 1.0f));

            if (isCalculating)
                currentBg = new Vector4(0.05f, 0.05f, 0.06f, 1.0f);

            // Draw Button Base
            drawList.AddRectFilled(buttonPos, buttonPos + buttonSize, Vec4ToU32(currentBg), 4f);
            
            // Outer Border
            uint borderColor = isHovered && !isCalculating ? ceruleumBlue : edgeHighlight;
            drawList.AddRect(buttonPos, buttonPos + buttonSize, borderColor, 4f, 0, isHovered && !isCalculating ? 2f : 1.5f);

            // Inner Shadow/Bevel Line
            drawList.AddLine(buttonPos + new Vector2(2, 2), new Vector2(buttonPos.X + buttonSize.X - 2, buttonPos.Y + 2), Vec4ToU32(new Vector4(1, 1, 1, 0.1f + hoverState * 0.1f)), 1f);

            if (isCalculating)
            {
                float time = (float)ImGui.GetTime();
                
                // Pulsing glow border
                float pulse = (float)Math.Sin(time * 6.0f) * 0.5f + 0.5f;
                drawList.AddRect(buttonPos, buttonPos + buttonSize, Vec4ToU32(new Vector4(0.0f, 0.8f, 1.0f, pulse * 0.6f)), 4f, 0, 2f);
                
                // Animated loading scanline over the button
                float scanX = (time * buttonSize.X * 1.5f) % (buttonSize.X * 2) - buttonSize.X;
                float startX = Math.Max(buttonPos.X, buttonPos.X + scanX);
                float endX = Math.Min(buttonPos.X + buttonSize.X, buttonPos.X + scanX + 60f);
                
                if (endX > startX)
                {
                    drawList.AddRectFilledMultiColor(
                        new Vector2(startX, buttonPos.Y + 2), 
                        new Vector2(endX, buttonPos.Y + buttonSize.Y - 2),
                        Vec4ToU32(new Vector4(0f, 0.8f, 1f, 0f)),
                        Vec4ToU32(new Vector4(0f, 0.8f, 1f, 0.4f)),
                        Vec4ToU32(new Vector4(0f, 0.8f, 1f, 0.4f)),
                        Vec4ToU32(new Vector4(0f, 0.8f, 1f, 0f))
                    );
                }

                // --- Side Animations ---
                // Space available on the left and right
                float sideSpace = buttonPos.X - pos.X;
                if (sideSpace > 50f)
                {
                    float yCenter = pos.Y + size.Y / 2;
                    float progress = (time * 1.5f) % 1.0f;
                    
                    // Left side animated bars moving towards the center
                    for (int i = 0; i < 3; i++)
                    {
                        float offset = (progress + i * 0.33f) % 1.0f; // 0 to 1
                        float x = pos.X + sideSpace * offset;
                        float alpha = (float)Math.Sin(offset * Math.PI); // Fade in and out
                        
                        drawList.AddRectFilled(
                            new Vector2(x, yCenter - 10f * alpha),
                            new Vector2(x + 15f, yCenter + 10f * alpha),
                            Vec4ToU32(new Vector4(0.0f, 0.8f, 1.0f, alpha * 0.7f))
                        );
                    }
                    
                    // Right side animated bars moving towards the center
                    for (int i = 0; i < 3; i++)
                    {
                        float offset = (progress + i * 0.33f) % 1.0f; // 0 to 1
                        float x = (pos.X + size.X) - sideSpace * offset - 15f;
                        float alpha = (float)Math.Sin(offset * Math.PI); // Fade in and out
                        
                        drawList.AddRectFilled(
                            new Vector2(x, yCenter - 10f * alpha),
                            new Vector2(x + 15f, yCenter + 10f * alpha),
                            Vec4ToU32(new Vector4(0.0f, 0.8f, 1.0f, alpha * 0.7f))
                        );
                    }

                    // Connecting lines from the edges to the button
                    drawList.AddLine(new Vector2(pos.X + 10f, yCenter), new Vector2(buttonPos.X - 10f, yCenter), Vec4ToU32(new Vector4(0.0f, 0.5f, 0.8f, 0.5f)), 2f);
                    drawList.AddLine(new Vector2(buttonPos.X + buttonSize.X + 10f, yCenter), new Vector2(pos.X + size.X - 10f, yCenter), Vec4ToU32(new Vector4(0.0f, 0.5f, 0.8f, 0.5f)), 2f);
                }
            }

            // Draw Button Text
            var textSize = ImGui.CalcTextSize(text);
            Vector2 textPos = buttonPos + new Vector2((buttonSize.X - textSize.X) / 2, (buttonSize.Y - textSize.Y) / 2);

            ImGui.SetCursorScreenPos(textPos);
            ImGui.TextColored(new Vector4(1, 1, 1, 0.9f), text);

            return isClicked;
        }
        public static bool DrawGarlondWarningButton(string id, Vector2 pos, Vector2 size, string text)
        {
            ImGui.SetCursorScreenPos(pos);
            bool isClicked = ImGui.InvisibleButton(id, size);
            bool isHovered = ImGui.IsItemHovered();

            uint baseId = ImGui.GetID(id);
            float hoverState = GetHoverState(baseId, isHovered, 10.0f);
            
            var drawList = ImGui.GetWindowDrawList();
            
            Vector4 baseBg = new Vector4(0.85f, 0.2f, 0.1f, 1.0f);
            Vector4 hoverBg = new Vector4(1.0f, 0.3f, 0.1f, 1.0f);
            Vector4 currentBg = LerpColor(baseBg, hoverBg, hoverState);
            
            drawList.AddRectFilled(pos, pos + size, Vec4ToU32(currentBg), 2f);
            drawList.AddRect(pos, pos + size, Vec4ToU32(new Vector4(0.4f, 0.1f, 0.05f, 1.0f)), 2f, 0, 1f);
            drawList.AddLine(pos + new Vector2(1, 1), new Vector2(pos.X + size.X - 1, pos.Y + 1), Vec4ToU32(new Vector4(1, 1, 1, 0.15f + hoverState * 0.2f)), 1f);

            if (hoverState > 0.01f)
            {
                Vector4 accentColor = LerpColor(new Vector4(1.0f, 0.8f, 0.0f, 0f), new Vector4(1.0f, 0.8f, 0.0f, 1.0f), hoverState);
                drawList.AddRectFilled(pos + new Vector2(0, size.Y * 0.2f), pos + new Vector2(3, size.Y * 0.8f), Vec4ToU32(accentColor), 1f);
            }

            var currentText = new Vector4(1f, 1f, 1f, 1f);
            var textSize = ImGui.CalcTextSize(text);
            Vector2 textPos = pos + new Vector2(size.X / 2 - textSize.X / 2, size.Y / 2 - textSize.Y / 2);
            drawList.AddText(textPos, Vec4ToU32(currentText), text);

            return isClicked;
        }

        public static bool DrawGarlondCheckbox(string id, Vector2 pos, ref bool isChecked)
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
            
            drawList.AddRectFilled(pos, pos + new Vector2(size, size), Vec4ToU32(darkIron), 3f);
            drawList.AddRect(pos, pos + new Vector2(size, size), Vec4ToU32(borderCol), 3f, 0, 1.5f);

            if (checkState > 0.01f)
            {
                float innerSize = size * 0.5f * checkState;
                Vector2 center = pos + new Vector2(size / 2, size / 2);
                drawList.AddRectFilled(center - new Vector2(innerSize / 2), center + new Vector2(innerSize / 2), Vec4ToU32(new Vector4(0.0f, 0.65f, 1.0f, checkState)), 2f);
                drawList.AddRectFilled(center - new Vector2(innerSize / 2 + 2), center + new Vector2(innerSize / 2 + 2), Vec4ToU32(new Vector4(0.0f, 0.65f, 1.0f, checkState * 0.4f)), 4f);
            }

            return isClicked;
        }

        public static bool DrawGarlondCheckboxWithText(string id, string text, ref bool isChecked)
        {
            Vector2 startPos = ImGui.GetCursorPos();
            bool result = DrawGarlondCheckbox(id, ImGui.GetCursorScreenPos(), ref isChecked);
            ImGui.SameLine(0, 10);
            ImGui.SetCursorPosY(startPos.Y + 2); // Center text vertically
            ImGui.Text(text);
            ImGui.Dummy(new Vector2(0, 4)); // Spacing between rows
            return result;
        }
        
        public static bool DrawGarlondRadioButton(string id, Vector2 pos, ref int currentVal, int targetVal)
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
            Vector4 borderCol = LerpColor(new Vector4(0.3f, 0.3f, 0.35f, 1.0f), new Vector4(0.13f, 0.77f, 0.36f, 1.0f), hoverState * 0.5f + selectState * 0.5f);
            drawList.AddCircle(center, radius, Vec4ToU32(borderCol), 0, 1.5f);

            if (selectState > 0.01f)
            {
                drawList.AddCircleFilled(center, radius * 0.5f * selectState, Vec4ToU32(new Vector4(0.13f, 0.77f, 0.36f, selectState)));
                drawList.AddCircleFilled(center, (radius * 0.5f + 2f) * selectState, Vec4ToU32(new Vector4(0.13f, 0.77f, 0.36f, selectState * 0.4f)));
            }
            
            return isClicked;
        }

        public static bool DrawGarlondRadioButtonWithText(string id, string text, ref int currentVal, int targetVal)
        {
            Vector2 startPos = ImGui.GetCursorPos();
            bool result = DrawGarlondRadioButton(id, ImGui.GetCursorScreenPos(), ref currentVal, targetVal);
            ImGui.SameLine(0, 10);
            ImGui.SetCursorPosY(startPos.Y + 1); // Center text vertically
            ImGui.Text(text);
            return result;
        }

        public static bool DrawGarlondInputText(string id, Vector2 pos, Vector2 size, ref string text, uint maxLength)
        {
            var drawList = ImGui.GetWindowDrawList();
            ImGui.SetCursorScreenPos(pos);
            
            // We use a dummy ID to track hover/active state of the input area bounds
            bool isHovered = ImGui.IsMouseHoveringRect(pos, pos + size);
            bool isActive = ImGui.IsItemActive(); // Works if called after InputText

            uint baseId = ImGui.GetID(id);
            float hoverState = GetHoverState(baseId, isHovered, 10.0f);
            float activeState = GetHoverState(baseId ^ 0x12345678, isActive, 10.0f);

            Vector4 bgCol = new Vector4(0.08f, 0.08f, 0.09f, 1.0f);
            Vector4 borderCol = LerpColor(new Vector4(0.3f, 0.3f, 0.35f, 1.0f), new Vector4(0.0f, 0.65f, 1.0f, 1.0f), hoverState * 0.5f + activeState * 0.5f);
            
            drawList.AddRectFilled(pos, pos + size, Vec4ToU32(bgCol), 4f);
            drawList.AddRect(pos, pos + size, Vec4ToU32(borderCol), 4f, 0, 1.5f);

            if (activeState > 0.01f)
            {
                drawList.AddRect(pos - new Vector2(1,1), pos + size + new Vector2(1,1), Vec4ToU32(new Vector4(0.0f, 0.65f, 1.0f, activeState * 0.3f)), 4f, 0, 2f);
            }

            ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0,0,0,0));
            ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0,0,0,0));
            ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new Vector4(0,0,0,0));
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.9f, 0.9f, 0.9f, 1.0f));
            
            ImGui.SetNextItemWidth(size.X);
            
            if (_inputBuffer == null) _inputBuffer = new byte[8192];
            Array.Clear(_inputBuffer, 0, _inputBuffer.Length);
            
            if (!string.IsNullOrEmpty(text))
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(text);
                Array.Copy(bytes, _inputBuffer, Math.Min(bytes.Length, _inputBuffer.Length - 1));
            }
            
            bool changed = ImGui.InputText(id, _inputBuffer);
            if (changed)
            {
                int nullIdx = Array.IndexOf(_inputBuffer, (byte)0);
                text = System.Text.Encoding.UTF8.GetString(_inputBuffer, 0, nullIdx >= 0 ? nullIdx : _inputBuffer.Length);
            }
            
            // Update active state based on actual input
            isActive = ImGui.IsItemActive();
            GetHoverState(ImGui.GetID(id) ^ 0x12345678, isActive, 10.0f); // update state

            ImGui.PopStyleColor(4);
            
            return changed;
        }

        public static bool DrawGarlondCollapsingHeader(string id, string text, ref bool isOpen)
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
            
            drawList.AddRectFilled(pos, pos + size, Vec4ToU32(currentBg), 4f);
            drawList.AddRect(pos, pos + size, Vec4ToU32(new Vector4(0.3f, 0.3f, 0.35f, 1.0f)), 4f, 0, 1f);

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

        public static bool DrawGarlondSwitch(string id, Vector2 pos, ref bool isChecked)
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
            Vector4 onBg = new Vector4(0.0f, 0.45f, 0.85f, 1.0f);
            Vector4 currentBg = LerpColor(offBg, onBg, checkState);
            
            // Draw pill background
            drawList.AddRectFilled(pos, pos + new Vector2(width, height), Vec4ToU32(currentBg), height * 0.5f);
            Vector4 borderCol = LerpColor(new Vector4(0.3f, 0.3f, 0.35f, 1.0f), new Vector4(0.0f, 0.65f, 1.0f, 1.0f), hoverState * 0.5f + checkState * 0.5f);
            drawList.AddRect(pos, pos + new Vector2(width, height), Vec4ToU32(borderCol), height * 0.5f, 0, 1.5f);

            // Draw handle
            float handleRadius = height * 0.5f - 3f;
            float handleX = Lerp(pos.X + height * 0.5f, pos.X + width - height * 0.5f, checkState);
            Vector2 handleCenter = new Vector2(handleX, pos.Y + height * 0.5f);
            
            drawList.AddCircleFilled(handleCenter, handleRadius + 1f, Vec4ToU32(new Vector4(0.05f, 0.05f, 0.05f, 0.5f))); // subtle drop shadow
            drawList.AddCircleFilled(handleCenter, handleRadius, Vec4ToU32(new Vector4(0.9f, 0.9f, 0.9f, 1.0f)));
            
            if (checkState > 0.01f) {
                drawList.AddCircleFilled(handleCenter, handleRadius * 0.5f, Vec4ToU32(new Vector4(0.0f, 0.65f, 1.0f, checkState)));
            }
            
            return isClicked;
        }

        public static bool DrawGarlondSwitchWithText(string id, string text, ref bool isChecked)
        {
            Vector2 startPos = ImGui.GetCursorPos();
            bool result = DrawGarlondSwitch(id, ImGui.GetCursorScreenPos(), ref isChecked);
            ImGui.SameLine(0, 10);
            ImGui.SetCursorPosY(startPos.Y + 2);
            ImGui.Text(text);
            ImGui.Dummy(new Vector2(0, 4));
            return result;
        }

        public static void BeginTooltip()
        {
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10, 10));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 8f);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1.5f);
            ImGui.PushStyleColor(ImGuiCol.PopupBg, new Vector4(0.06f, 0.06f, 0.09f, 0.98f));
            ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.0f, 0.65f, 1.0f, 0.6f));
            
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
            
            // Elegant premium solution: Draw a neon blue/cyan bottom edge line indicating more content below
            Vector4 neonGlow = new Vector4(0.0f, 0.65f, 1.0f, 0.5f); // Ironworks cyan glow
            Vector4 darkShadow = new Vector4(0.0f, 0.0f, 0.0f, 0.6f);
            Vector4 transparent = new Vector4(0.0f, 0.0f, 0.0f, 0.0f);
            
            // Subtle black inner shadow coming UP from the bottom edge
            drawList.AddRectFilledMultiColor(
                new Vector2(min.X, max.Y - fadeHeight),
                max,
                Vec4ToU32(transparent),
                Vec4ToU32(transparent),
                Vec4ToU32(darkShadow),
                Vec4ToU32(darkShadow)
            );
            
            // Sleek glowing neon line directly on the bottom edge
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
            drawList.AddRectFilled(modalPos, modalPos + currentSize, ImGui.ColorConvertFloat4ToU32(bgColor), 8f);
            drawList.AddRect(modalPos, modalPos + currentSize, ImGui.ColorConvertFloat4ToU32(new Vector4(0.79f, 0.66f, 0.41f, 0.3f * alpha)), 8f, 0, 2f);
            
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

