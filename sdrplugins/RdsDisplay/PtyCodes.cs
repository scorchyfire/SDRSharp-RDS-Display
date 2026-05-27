namespace SDRSharp.RdsDisplay
{
    public static class PtyCodes
    {
        private static readonly string[] GlobalMap =
        {
            "No PTY",
            "News",
            "Current Affairs",
            "Information",
            "Sports",
            "Education",
            "Drama",
            "Culture",
            "Science",
            "Varied",
            "Pop Music",
            "Rock Music",
            "Easy Listening",
            "Light Classical",
            "Serious Classical",
            "Other Music",
            "Weather",
            "Finance",
            "Children's Programmes",
            "Social Affairs",
            "Religion",
            "Phone-in",
            "Travel",
            "Leisure",
            "Jazz Music",
            "Country Music",
            "National Music",
            "Oldies Music",
            "Folk Music",
            "Documentary",
            "Alarm Test",
            "Alarm"
        };

        private static readonly string[] NorthAmericaMap =
        {
            "No PTY",
            "News",
            "Information",
            "Sports",
            "Talk",
            "Rock",
            "Classic Rock",
            "Adult Hits",
            "Soft Rock",
            "Top 40",
            "Country",
            "Oldies",
            "Soft Music",
            "Nostalgia",
            "Jazz",
            "Classical",
            "Rhythm & Blues",
            "Soft Rhythm & Blues",
            "Language",
            "Religious Music",
            "Religious Talk",
            "Personality",
            "Public",
            "College",
            "Spanish Talk",
            "Spanish Music",
            "Hip Hop",
            "Unassigned",
            "",
            "Weather",
            "Emergency Test",
            "Emergency"
        };

        public static string GetProgrammeType(int ptyCode, bool useNorthAmerica)
        {
            if (ptyCode < 0 || ptyCode > 31)
                return $"Unknown ({ptyCode})";

            string[] map = useNorthAmerica ? NorthAmericaMap : GlobalMap;
            string name = map[ptyCode];
            if (string.IsNullOrEmpty(name))
                return "";
            return $"{name} ({ptyCode})";
        }
    }
}
