namespace XIVHubCompanion.Collections
{
    public class CollectionItem
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public uint IconId { get; set; }
        public bool IsUnlocked { get; set; }
        public CollectionCategory Category { get; set; }
        public string Subcategory { get; set; }
        public string[] Sources { get; set; }
        /// <summary>Achievements: the type (AchievementKind: Battle, Quests, ...); Subcategory is the category inside it.</summary>
        public string Group { get; set; } = "";
        /// <summary>Achievement points from the game sheet.</summary>
        public int Points { get; set; }
    }
}
