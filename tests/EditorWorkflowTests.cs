using CardFileMaker;

public static class EditorWorkflowTests
{

    public static void Run(Action<string, Action> check)
    {
        check("引用候选显示ID与实际名称", () =>
        {
            Require(new ReferenceChoice(14, "フェス会場").ToString() == "14 · フェス会場", "引用显示文本不一致");
        });

        string game = Environment.GetEnvironmentVariable("CFM_TEST_GAME") ?? "";
        string opt = Environment.GetEnvironmentVariable("CFM_TEST_OPT") ?? "";
        if (!Directory.Exists(game) || !Directory.Exists(opt))
        {
            Console.WriteLine("SKIP 游戏目录只读校验：未设置有效的 CFM_TEST_GAME 和 CFM_TEST_OPT。");
            return;
        }

        GameCatalog? catalog = null;
        check("只读加载游戏库并保留完整ID和资源名称查重集合", () =>
        {
            catalog = GameCatalog.Load(game, opt, scanCabs: false);
            Require(catalog.Cards.Count > 0 && catalog.Characters.Count > 0, "卡片或角色库为空");
            Require(catalog.Cards.All(x => catalog.Ids.CardIds.Contains(x.Id)), "卡片查重集合丢失ID");
            Require(catalog.Characters.All(x => catalog.Ids.CharaIds.Contains(x.Id)), "角色查重集合丢失ID");
            Require(catalog.Get("SkillID").All(x => catalog.Ids.SkillIds.Contains(x.Id)), "技能查重集合丢失ID");
            Require(catalog.Ids.AssetNames.Count > 0 && catalog.Ids.Cabs.Count == 0, "scanCabs=false 行为不一致");
        });
        if (catalog is null) return;
        var loaded = catalog;

        check("舞台14与版本1045来自实际游戏数据", () =>
        {
            Require(loaded.Get("StageID").Any(x => x.Id == 14 && x.Name == "フェス会場"), "没有读取舞台14");
            Require(loaded.Get("VersionID").Any(x => x.Id == 1045 && x.Name == "ver_1_45" && x.Detail.Contains("bright MEMORY Act.3")), "没有读取版本1045的定义");
        });
        check("音乐引用与学校候选均可读取", () =>
        {
            foreach (var field in new[] { "Genre", "FirstChapterName", "MusicRightsName", "MusicSourceName", "ArtistName" })
                Require(loaded.Get(field).Count > 0, field + "候选为空");
            foreach (var id in new[] { 0, 1, 999 })
                Require(loaded.Get("School").Any(x => x.Id == id), "缺少学校" + id);
            Require(loaded.Get("School").Any(x => x.Id == 999 && x.Name == "プロモーション"), "学校显示名丢失");
            Require(loaded.Get("不存在的字段").Count == 0, "未知引用字段不应返回其它字段");
        });
        check("官方与自制opt来源分组互不合并", () =>
        {
            Require(loaded.Cards.Any(x => x.Group == "官方游戏包"), "缺少官方卡片");
            var custom = loaded.Cards.Where(x => x.Group == "自制 opt").ToList();
            Require(custom.All(x => PathSafety.Within(x.CardPath, opt) && x.Source == Path.GetFileName(opt)), "自制卡片来源错误");
            Require(loaded.Cards.GroupBy(x => (x.Group, x.Id)).All(x => x.Count() == 1), "同组卡片ID未按包顺序覆盖");
            Require(loaded.Characters.GroupBy(x => (x.Group, x.Id)).All(x => x.Count() == 1), "同组角色ID未按包顺序覆盖");
            Require(loaded.Cards.All(x => !x.CardPath.Contains("backup", StringComparison.OrdinalIgnoreCase)), "备份目录被当作有效卡片");
        });
        check("角色来自Chara定义且卡片保留真实角色引用", () =>
        {
            string cardRoot = Path.Combine(opt, "card");
            string? cardPath = Directory.Exists(cardRoot)
                ? Directory.EnumerateDirectories(cardRoot, "card*").OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .Select(x => Path.Combine(x, "Card.xml")).FirstOrDefault(File.Exists)
                : null;
            if (cardPath is null)
            {
                Console.WriteLine("SKIP 所选opt角色对照：当前opt没有Card.xml。");
                return;
            }
            var cardXml = Catalog.ReadXml(cardPath).Root!;
            int cardId = int.Parse(cardXml.Element("Name")!.Element("id")!.Value);
            int charaId = int.Parse(cardXml.Element("CharaID")!.Element("id")!.Value);
            string charaName = cardXml.Element("CharaID")!.Element("str")!.Value;
            var card = loaded.Cards.Single(x => x.Id == cardId && x.Group == "自制 opt");
            Require(card.CharaId == charaId && card.CharaName == charaName && card.CardPath.Equals(cardPath, StringComparison.OrdinalIgnoreCase), "卡片角色引用与原XML不一致");
            string charaRoot = Path.Combine(opt, "chara");
            var charaDefinitions = Directory.Exists(charaRoot)
                ? Directory.EnumerateDirectories(charaRoot, "chara*").OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .Select(x => Path.Combine(x, "Chara.xml")).Where(File.Exists).Select(x => Catalog.ReadXml(x).Root!).ToList()
                : [];
            foreach (var definition in charaDefinitions)
            {
                int id = int.Parse(definition.Element("Name")!.Element("id")!.Value);
                string name = definition.Element("Name")!.Element("str")!.Value;
                Require(loaded.Characters.Any(x => x.Id == id && x.Name == name && x.Group == "自制 opt"), "自制角色定义与原XML不一致：" + id);
            }
            Require(loaded.Characters.Where(x => x.Group == "自制 opt").All(x => charaDefinitions.Any(d => d.Element("Name")!.Element("id")!.Value == x.Id.ToString())), "把卡片引用编造成了角色定义");
            if (!loaded.Characters.Any(x => x.Id == charaId))
                Require(loaded.Warnings.Any(x => x.Contains(cardPath) && x.Contains("Chara.xml")), "缺少角色定义时没有路径诊断");
        });
        check("技能配对使用Card实际引用而非ID加一", () =>
        {
            Require(loaded.SkillPairs.TryGetValue(100005, out var targets) && targets.Contains(100046), "缺少100005到100046的实际配对");
            foreach (var pair in loaded.SkillPairs)
            {
                Require(loaded.Get("SkillID").Any(x => x.Id == pair.Key), "基础技能定义不存在");
                Require(pair.Value.Count == pair.Value.Distinct().Count(), "技能配对重复");
                Require(pair.Value.All(id => loaded.Get("SkillID").Any(x => x.Id == id)), "超解花技能定义不存在");
            }
        });

        string insideOpt = Path.Combine(game, "option", "A017");
        if (Directory.Exists(insideOpt))
            check("游戏option内所选opt只归入自制组", () =>
            {
                var inside = GameCatalog.Load(game, insideOpt, scanCabs: false);
                Require(inside.Cards.Any(x => x.Group == "自制 opt" && PathSafety.Within(x.CardPath, insideOpt)), "所选opt未进入自制组");
                Require(!inside.Cards.Any(x => x.Group == "官方游戏包" && PathSafety.Within(x.CardPath, insideOpt)), "所选opt重复进入官方组");
                Require(inside.Cards.Where(x => x.Group == "自制 opt").All(x => x.Source == "A017"), "自制来源未保留包名");
            });
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
