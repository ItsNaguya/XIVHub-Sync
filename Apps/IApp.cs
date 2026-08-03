namespace XIVHubCompanion.Apps
{
    public interface IApp
    {
        string Name { get; }
        string Icon { get; }
        bool HasSettings { get; }
        void Draw();
        void DrawSettings();
        void Update();
        void Dispose();
    }
}
