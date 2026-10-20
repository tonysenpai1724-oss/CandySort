using System;

namespace CandySort
{
    /// <summary>Immutable generation parameters for one authored campaign stage.</summary>
    public sealed class CandySortLevelDefinition
    {
        public int Level { get; }
        public int OrderCount { get; }
        public int FlavorCount { get; }
        public int ActivePlates { get; }
        public bool IsHard { get; }
        public int Seed { get; }
        public int MinBubbleSize { get; }
        public int MaxBubbleSize { get; }
        /// <summary>Unserved opening candies paired with up to two candies per opening plate in 2–3 candy groups.</summary>
        public int OpeningTrayCandies { get; }
        /// <summary>Percentage chance (0..100) to select a flavor different from a bubble's leading candy.</summary>
        public int Mixing { get; }
        public string DifficultyLabel { get; }
        public string HelpText { get; }

        internal CandySortLevelDefinition(int level, int orderCount, int flavorCount,
            int activePlates, int seed, int minBubbleSize, int maxBubbleSize, int mixing,
            string difficultyLabel, string helpText, int openingTrayCandies = 0)
        {
            Level = level;
            OrderCount = orderCount;
            FlavorCount = flavorCount;
            ActivePlates = activePlates;
            IsHard = level % 5 == 0;
            Seed = seed;
            MinBubbleSize = minBubbleSize;
            MaxBubbleSize = maxBubbleSize;
            OpeningTrayCandies = openingTrayCandies;
            Mixing = mixing;
            DifficultyLabel = difficultyLabel;
            HelpText = helpText;
        }
    }

    /// <summary>The finite authored campaign. Construction never searches for playable arrangements.</summary>
    public static class CandySortCampaign
    {
        public const int LevelCount = 30;
        private static readonly CandySortLevelDefinition[] levels =
        {
            // Level, orders, flavors, plates, seed, bubble min/max, mixing %, label, help,
            // optional unserved opening candy count (zero unless explicitly authored).
            new CandySortLevelDefinition(1, 27, 2, 2, 11003, 3, 3, 0, "LÀM QUEN",
                "Chạm kẹo cùng loại với đĩa. Đủ 3 viên là xong một đơn."),
            new CandySortLevelDefinition(2, 28, 3, 2, 22003, 2, 3, 15, "LÀM QUEN",
                "Kẹo chưa có đĩa sẽ chờ trong khay tạm có 5 ô."),
            new CandySortLevelDefinition(3, 29, 3, 3, 33013, 2, 4, 20, "LÀM QUEN",
                "Đã mở đĩa thứ 3! Ghép đủ 3 viên để hoàn thành đơn."),
            new CandySortLevelDefinition(4, 30, 4, 3, 44017, 2, 5, 30, "DỄ",
                "Tính trước loại kẹo tiếp theo, giữ chỗ trong khay tạm."),
            new CandySortLevelDefinition(5, 40, 6, 4, 55000, 2, 5, 75, "MÀN KHÓ",
                "Mở đĩa thứ 4! Dùng khay tạm để dọn nhóm kẹo trộn phía dưới.", 15),
            new CandySortLevelDefinition(6, 41, 7, 4, 66029, 2, 5, 25, "DỄ",
                "Thêm kẹo cam gói giấy! Ưu tiên kẹo khớp với 4 đĩa."),
            new CandySortLevelDefinition(7, 42, 8, 4, 77003, 2, 5, 35, "DỄ",
                "8 loại kẹo, 4 đĩa. Nhớ chừa chỗ trong khay tạm."),
            new CandySortLevelDefinition(8, 43, 9, 4, 88001, 2, 5, 40, "DỄ",
                "Thêm thanh sô-cô-la! Hoàn thành đĩa đang dở để đón đơn mới."),
            new CandySortLevelDefinition(9, 44, 10, 4, 99013, 2, 5, 45, "TRUNG BÌNH",
                "Kẹo dẻo trắng đã xuất hiện. Để ý cả kẹo đang chờ trong khay."),
            new CandySortLevelDefinition(10, 45, 12, 4, 100003, 2, 5, 80, "MÀN KHÓ",
                "12 loại kẹo! Dùng khay tạm mở đường qua nhóm kẹo trộn.", 16),
            new CandySortLevelDefinition(11, 46, 13, 4, 110017, 2, 5, 45, "TRUNG BÌNH",
                "Thêm bánh tart sô-cô-la! Gom đủ bộ 3 để nhường đĩa cho đơn mới."),
            new CandySortLevelDefinition(12, 47, 14, 4, 120011, 2, 5, 50, "TRUNG BÌNH",
                "Lấp đầy đĩa đang dở, tránh giữ nhiều kẹo chưa có đĩa."),
            new CandySortLevelDefinition(13, 48, 15, 4, 130003, 2, 5, 55, "TRUNG BÌNH",
                "Bong bóng nhiều loại kẹo: hãy tính trước đĩa kế tiếp."),
            new CandySortLevelDefinition(14, 49, 16, 4, 140009, 2, 5, 60, "TRUNG BÌNH",
                "Kẹo dâu gói giấy đã mở. Chừa chỗ trước khi lấy nhóm kẹo trộn."),
            new CandySortLevelDefinition(15, 50, 17, 4, 150001, 2, 5, 85, "MÀN KHÓ",
                "17 loại kẹo, khay chỉ có 5 ô. Ưu tiên hoàn thành đơn!", 14),
            new CandySortLevelDefinition(16, 51, 18, 4, 160001, 2, 5, 55, "TRUNG BÌNH",
                "Thêm kẹo bông gòn! Để dành khay cho kẹo chưa có đĩa."),
            new CandySortLevelDefinition(17, 52, 19, 4, 170003, 2, 5, 60, "TRUNG BÌNH",
                "Gom đủ bộ 3 thay vì chất thêm kẹo vào khay tạm."),
            new CandySortLevelDefinition(18, 53, 20, 4, 180001, 2, 5, 65, "TRUNG BÌNH",
                "Thêm gấu xanh! Chọn đĩa sắp hoàn thành trước khi giữ loại kẹo mới."),
            new CandySortLevelDefinition(19, 54, 21, 4, 190026, 2, 5, 70, "TRUNG BÌNH",
                "Luân phiên lấp đầy cả 4 đĩa khi nhóm kẹo càng nhiều loại."),
            new CandySortLevelDefinition(20, 55, 22, 4, 200002, 2, 5, 90, "MÀN KHÓ",
                "22 loại kẹo cùng chờ! Dùng khay tạm thật khéo.", 16),
            new CandySortLevelDefinition(21, 56, 23, 4, 210011, 2, 5, 65, "THỬ THÁCH",
                "Tính trước vài bước, giữ lại ô trống cuối cùng trong khay."),
            new CandySortLevelDefinition(22, 57, 24, 4, 220011, 2, 5, 70, "THỬ THÁCH",
                "Xong một đĩa có thể giải phóng nhiều kẹo trong khay."),
            new CandySortLevelDefinition(23, 58, 25, 4, 230001, 2, 5, 75, "THỬ THÁCH",
                "Thêm kẹo cam kèm viên xanh. Cân bằng đĩa đang dở với khay tạm."),
            new CandySortLevelDefinition(24, 59, 26, 4, 240007, 2, 5, 80, "THỬ THÁCH",
                "Lấy kẹo khớp đĩa trước khi cất loại mới vào khay."),
            new CandySortLevelDefinition(25, 60, 27, 4, 250007, 2, 5, 95, "MÀN KHÓ",
                "27 loại kẹo, 60 đơn! Hãy tận dụng từng ô khay.", 14),
            new CandySortLevelDefinition(26, 61, 28, 4, 260003, 2, 5, 75, "THỬ THÁCH",
                "Thêm kẹo dẻo hình cá! Luôn nhắm tới bộ 3 tiếp theo."),
            new CandySortLevelDefinition(27, 62, 29, 4, 270064, 2, 5, 80, "THỬ THÁCH",
                "Hoàn thành đơn để dọn khay rồi mới lấy kẹo chưa có đĩa."),
            new CandySortLevelDefinition(28, 63, 30, 4, 280007, 2, 5, 85, "THỬ THÁCH",
                "Kẹo trộn nhiều loại: đừng để khay đầy kẹo chưa có đĩa."),
            new CandySortLevelDefinition(29, 64, 31, 4, 290047, 2, 5, 90, "THỬ THÁCH",
                "Sắp đến màn cuối! Hoàn thành đơn, giữ khay thông thoáng."),
            new CandySortLevelDefinition(30, 65, 32, 4, 299941, 2, 5, 95, "MÀN KHÓ",
                "Màn cuối: đủ 32 loại kẹo, 65 đơn và chỉ 5 ô khay tạm!", 15)
        };

        public static CandySortLevelDefinition GetLevel(int level)
        {
            if (level < 1 || level > LevelCount)
                throw new ArgumentOutOfRangeException(nameof(level), level,
                    "Campaign levels must be between 1 and " + LevelCount + ".");
            return levels[level - 1];
        }
    }
}
