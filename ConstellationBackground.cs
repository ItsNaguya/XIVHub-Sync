using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace XIVHubCompanion
{
    public class Particle
    {
        public Vector2 Position;
        public Vector2 Velocity;
        public float Radius;
        public float Alpha;
    }

    public class DataStream
    {
        public Vector2 Position;
        public float Speed;
        public float Length;
        public float Alpha;
    }

    public class ConstellationBackground
    {
        private List<Particle> _particles = new List<Particle>();
        private List<DataStream> _streams = new List<DataStream>();
        private Random _rnd = new Random();
        private int _maxParticles = 80;
        private int _maxStreams = 30;
        private float _connectionDistance = 150.0f;
        private bool _initialized = false;
        private Vector2 _screenSize;
        private float _scanlineY = -100f;

        public void Draw(Vector2 pos, Vector2 size, bool hideScanline = false)
        {
            if (!_initialized || size != _screenSize)
            {
                Initialize(size);
                _screenSize = size;
                _initialized = true;
            }

            UpdateParticles(size);
            
            float dt = ImGui.GetIO().DeltaTime;
            
            // Scanline movement
            float scanTail = 120f;
            float scanFront = 20f;
            _scanlineY += 60f * dt;
            if (_scanlineY > size.Y + scanTail) _scanlineY = -scanFront; // wrap around smoothly

            var drawList = ImGui.GetWindowDrawList();
            Vector2 mousePos = ImGui.GetMousePos();
            bool isMouseHovering = ImGui.IsMouseHoveringRect(pos, pos + size);

            // Restrict all background rendering strictly to the content area
            ImGui.PushClipRect(pos, pos + size, true);

            // 1. Draw Data Streams (Falling matrix-like lines)
            foreach (var stream in _streams)
            {
                stream.Position.Y += stream.Speed * dt;
                if (stream.Position.Y > size.Y + stream.Length)
                {
                    stream.Position.Y = -stream.Length;
                    stream.Position.X = (float)_rnd.NextDouble() * size.X;
                }

                Vector2 streamStart = pos + new Vector2(stream.Position.X, stream.Position.Y - stream.Length);
                Vector2 streamEnd = pos + stream.Position;
                
                uint streamTopCol = ImGui.ColorConvertFloat4ToU32(new Vector4(0.0f, 0.65f, 1.0f, 0.0f));
                uint streamBotCol = ImGui.ColorConvertFloat4ToU32(new Vector4(0.0f, 0.8f, 1.0f, stream.Alpha));
                
                drawList.AddRectFilledMultiColor(
                    new Vector2(streamStart.X, streamStart.Y),
                    new Vector2(streamEnd.X + 1f, streamEnd.Y), // 1px width
                    streamTopCol, streamTopCol, streamBotCol, streamBotCol
                );
            }

            // 2. Draw Scanline (Gradient fading out at top and bottom)
            if (!hideScanline)
            {
                float scanAlpha = 0.15f;
                uint scanClear = ImGui.ColorConvertFloat4ToU32(new Vector4(0.0f, 0.65f, 1.0f, 0.0f));
                uint scanSolid = ImGui.ColorConvertFloat4ToU32(new Vector4(0.0f, 0.65f, 1.0f, scanAlpha));
                uint scanBright = ImGui.ColorConvertFloat4ToU32(new Vector4(0.0f, 0.8f, 1.0f, scanAlpha * 2f));
                
                // Tail (fades out upwards)
                drawList.AddRectFilledMultiColor(
                    pos + new Vector2(0, _scanlineY - scanTail),
                    pos + new Vector2(size.X, _scanlineY),
                    scanClear, scanClear, scanSolid, scanSolid
                );
                
                // Core Bright Line
                drawList.AddRectFilled(
                    pos + new Vector2(0, _scanlineY - 1f),
                    pos + new Vector2(size.X, _scanlineY + 1f),
                    scanBright
                );
                
                // Front (fades out downwards)
                drawList.AddRectFilledMultiColor(
                    pos + new Vector2(0, _scanlineY),
                    pos + new Vector2(size.X, _scanlineY + scanFront),
                    scanSolid, scanSolid, scanClear, scanClear
                );
            }

            // 3. Draw nodes and connections
            for (int i = 0; i < _particles.Count; i++)
            {
                var p = _particles[i];
                Vector2 globalPos = pos + p.Position;
                
                float glowMultiplier = 1.0f;
                
                if (!hideScanline)
                {
                    // Smoothly pulse the node based on distance to the scanline
                    float distToScanline = Math.Abs(p.Position.Y - _scanlineY);
                    float maxGlowDist = 80f;
                    
                    if (distToScanline < maxGlowDist)
                    {
                        // Linear interpolation: 1.0 at distance 80, up to 2.5 at distance 0
                        float intensity = 1.0f - (distToScanline / maxGlowDist);
                        // Sine curve for smoother ease-in and ease-out
                        float smoothIntensity = (float)Math.Sin(intensity * Math.PI / 2);
                        glowMultiplier = 1.0f + (1.5f * smoothIntensity);
                    }
                }
                
                float finalAlpha = Math.Clamp(p.Alpha * glowMultiplier, 0f, 1f);
                
                // Draw particle (Ceruleum Blue)
                drawList.AddCircleFilled(globalPos, p.Radius * glowMultiplier, ImGui.ColorConvertFloat4ToU32(new Vector4(0.0f, 0.65f, 1.0f, finalAlpha)));

                // Draw connections
                for (int j = i + 1; j < _particles.Count; j++)
                {
                    var p2 = _particles[j];
                    float dist = Vector2.Distance(p.Position, p2.Position);
                    
                    if (dist < _connectionDistance)
                    {
                        float alpha = (1.0f - (dist / _connectionDistance)) * 0.4f * glowMultiplier;
                        drawList.AddLine(globalPos, pos + p2.Position, ImGui.ColorConvertFloat4ToU32(new Vector4(0.0f, 0.55f, 0.9f, alpha)), 1.0f);
                    }
                }

                // Interactive mouse connection
                if (isMouseHovering)
                {
                    float distMouse = Vector2.Distance(globalPos, mousePos);
                    if (distMouse < 180.0f)
                    {
                        float alpha = (1.0f - (distMouse / 180.0f)) * 0.6f;
                        drawList.AddLine(globalPos, mousePos, ImGui.ColorConvertFloat4ToU32(new Vector4(0.2f, 0.8f, 1.0f, alpha)), 1.5f);
                    }
                }
            }

            ImGui.PopClipRect();
        }

        private void Initialize(Vector2 size)
        {
            _particles.Clear();
            for (int i = 0; i < _maxParticles; i++)
            {
                _particles.Add(new Particle
                {
                    Position = new Vector2((float)_rnd.NextDouble() * size.X, (float)_rnd.NextDouble() * size.Y),
                    Velocity = new Vector2(((float)_rnd.NextDouble() - 0.5f) * 20f, ((float)_rnd.NextDouble() - 0.5f) * 20f),
                    Radius = (float)_rnd.NextDouble() * 1.5f + 1.0f,
                    Alpha = (float)_rnd.NextDouble() * 0.4f + 0.15f
                });
            }

            _streams.Clear();
            for (int i = 0; i < _maxStreams; i++)
            {
                _streams.Add(new DataStream
                {
                    Position = new Vector2((float)_rnd.NextDouble() * size.X, (float)_rnd.NextDouble() * size.Y),
                    Speed = (float)_rnd.NextDouble() * 150f + 50f,
                    Length = (float)_rnd.NextDouble() * 80f + 20f,
                    Alpha = (float)_rnd.NextDouble() * 0.15f + 0.05f
                });
            }
        }

        private void UpdateParticles(Vector2 size)
        {
            float dt = ImGui.GetIO().DeltaTime;
            foreach (var p in _particles)
            {
                p.Position += p.Velocity * dt;

                // Bounce off walls
                if (p.Position.X < 0 || p.Position.X > size.X) p.Velocity.X *= -1;
                if (p.Position.Y < 0 || p.Position.Y > size.Y) p.Velocity.Y *= -1;

                // Clamp to prevent escaping
                p.Position.X = Math.Clamp(p.Position.X, 0, size.X);
                p.Position.Y = Math.Clamp(p.Position.Y, 0, size.Y);
            }
        }
    }
}
