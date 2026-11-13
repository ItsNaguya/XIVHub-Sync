namespace XIVHubCompanion.Apps
{
    public interface IApp
    {
        string Name { get; }
        string Icon { get; }
        bool HasSettings { get; }
        /// <summary>True once the app's layout fits the tablet's larger text (Fonts.TabletText); until then it draws at the old size.</summary>
        bool LargeText => false;
        void Draw();
        void DrawSettings();
        void Update();
        void Dispose();
    }
}
