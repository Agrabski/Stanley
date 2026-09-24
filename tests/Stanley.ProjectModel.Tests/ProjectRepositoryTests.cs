using Stanley.ProjectModel.Backgrounds;
using Stanley.ProjectModel.Bubbles;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;
using Stanley.ProjectModel.Poses;
using Stanley.ProjectModel.Props;
using Stanley.ProjectModel.Storage;

namespace Stanley.ProjectModel.Tests;

public class ProjectRepositoryTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("stanley-repo-tests").FullName;

    [Fact]
    public void Initialize_creates_the_manifest_top_level_folders_and_gitattributes()
    {
        var repository = ProjectRepository.Initialize(_root, "My Comic", new PageTrim(new PageSize(210, 297), 3));

        Assert.True(File.Exists(Path.Combine(_root, "stanley.json")));
        Assert.True(File.Exists(Path.Combine(_root, ".gitattributes")));
        foreach (var folder in new[] { "characters", "poses", "props", "backgrounds", "issues" })
            Assert.True(Directory.Exists(Path.Combine(_root, folder)), $"expected {folder}/ to exist");

        var manifest = repository.LoadManifest();
        Assert.Equal("My Comic", manifest.Title);
        Assert.Empty(manifest.IssueIds);
    }

    [Fact]
    public void Character_definition_revision_sticker_and_stretch_region_round_trip_and_land_on_the_documented_layout()
    {
        var repository = ProjectRepository.Initialize(_root, "My Comic", new PageTrim(new PageSize(210, 297), 3));

        var characterId = CharacterId.New();
        var stickerId = StickerId.New();
        var character = new CharacterDefinition(
            characterId,
            "Alice",
            BodyPresets.Shape(BodyPreset.Heroic),
            new Skeleton([new ViewAngleRestLayout(ViewAngle.Front, [new BoneRestPose(HumanoidBone.Hips, new Point2D(0, 0))])]),
            new SortedDictionary<string, ColorValue> { ["Skin"] = ColorValue.FromHex("#f0c8a0") },
            new SortedDictionary<string, StickerSlotDefinition> { ["torso"] = new StickerSlotDefinition(0, [stickerId]) });
        repository.SaveCharacter(character);

        var sticker = new Sticker(stickerId, "Jacket A", "torso", StickerKind.BuildStretch, ["default"]);
        repository.SaveSticker(characterId, sticker);

        var stretch = new StretchRegion(4, 2, 4, 2);
        repository.SaveStretchRegion(characterId, stickerId, stretch);

        var revisionId = CharacterRevisionId.New();
        var revision = new CharacterRevision(
            revisionId,
            characterId,
            "Default",
            new SortedDictionary<string, IReadOnlyList<StickerId>> { ["torso"] = [stickerId] },
            new SortedDictionary<string, ColorValue>(),
            ProportionOverride: null,
            Build: 0.5);
        repository.SaveCharacterRevision(revision);

        Assert.Equivalent(character, repository.LoadCharacter(characterId), strict: true);
        Assert.Equivalent(sticker, repository.LoadSticker(characterId, stickerId), strict: true);
        Assert.Equivalent(stretch, repository.LoadStretchRegion(characterId, stickerId), strict: true);
        Assert.Equivalent(revision, repository.LoadCharacterRevision(characterId, revisionId), strict: true);

        var characterDir = Path.Combine(_root, "characters", $"{characterId.Value}-alice");
        Assert.True(Directory.Exists(characterDir));
        Assert.True(File.Exists(Path.Combine(characterDir, "character.json")));
        Assert.True(File.Exists(Path.Combine(characterDir, "revisions", $"{revisionId.Value}-default.json")));

        var stickerDir = Path.Combine(characterDir, "stickers", $"{stickerId.Value}-jacket-a");
        Assert.True(File.Exists(Path.Combine(stickerDir, "sticker.json")));
        Assert.True(File.Exists(Path.Combine(stickerDir, "stretch.json")));
        Assert.True(Directory.Exists(Path.Combine(stickerDir, "variants")));
    }

    [Fact]
    public void Saving_again_after_a_rename_keeps_the_original_folder_slug()
    {
        var repository = ProjectRepository.Initialize(_root, "My Comic", new PageTrim(new PageSize(210, 297), 3));
        var characterId = CharacterId.New();
        var skeleton = new Skeleton([]);

        repository.SaveCharacter(new CharacterDefinition(characterId, "Alice", BodyShape.Default, skeleton, [], []));
        var originalDir = Path.Combine(_root, "characters", $"{characterId.Value}-alice");
        Assert.True(Directory.Exists(originalDir));

        repository.SaveCharacter(new CharacterDefinition(characterId, "Alicia", BodyShape.Default, skeleton, [], []));

        Assert.True(Directory.Exists(originalDir), "renaming should not move the entity's folder");
        Assert.Equal("Alicia", repository.LoadCharacter(characterId).Name);
    }

    [Fact]
    public void Pose_prop_and_background_round_trip()
    {
        var repository = ProjectRepository.Initialize(_root, "My Comic", new PageTrim(new PageSize(210, 297), 3));

        var pose = new Pose(
            PoseId.New(),
            "Wave",
            new PoseData(ViewAngle.Front, [new BoneRotation(HumanoidBone.RightUpperArm, 45)], new SortedDictionary<string, string> { ["eyes"] = "neutral" }));
        repository.SavePose(pose);
        Assert.Equivalent(pose, repository.LoadPose(pose.Id), strict: true);

        var prop = new Prop(PropId.New(), "Round Table", new Point2D(50, 50), ["default"]);
        repository.SaveProp(prop);
        Assert.Equivalent(prop, repository.LoadProp(prop.Id), strict: true);

        var backdropId = BackdropId.New();
        var background = new Background(BackgroundId.New(), "Alice's Apartment", [new BackdropAsset(backdropId, "Wall and Floor")]);
        repository.SaveBackground(background);
        Assert.Equivalent(background, repository.LoadBackground(background.Id), strict: true);

        var revision = new BackgroundRevision(
            BackgroundRevisionId.New(),
            background.Id,
            "Before Renovation",
            new BackgroundLayer(backdropId, [new PropPlacement(PropPlacementId.New(), prop.Id, "default", new Point2D(10, 10), new Point2D(1, 1), 0)]),
            Front: null);
        repository.SaveBackgroundRevision(revision);
        Assert.Equivalent(revision, repository.LoadBackgroundRevision(background.Id, revision.Id), strict: true);
    }

    [Fact]
    public void Issue_page_and_panel_round_trip_and_panel_file_has_no_slug()
    {
        var repository = ProjectRepository.Initialize(_root, "My Comic", new PageTrim(new PageSize(210, 297), 3));

        var characterId = CharacterId.New();
        var revisionId = CharacterRevisionId.New();
        var pageId = PageId.New();
        var panelId = PanelId.New();

        var issue = new Issue(
            IssueId.New(),
            "1",
            "Issue One",
            [pageId],
            new SortedDictionary<CharacterId, CharacterRevisionId> { [characterId] = revisionId });
        repository.SaveIssue(issue);

        var page = new Page(pageId, "Splash", TrimOverride: null, [panelId]);
        repository.SavePage(issue.Id, page);

        var panel = new Panel(
            panelId,
            new PanelShape([new ShapeAnchor(new Point2D(0, 0), new Point2D(0, 0), new Point2D(10, 0), AnchorHandleKind.Corner)]),
            new InlineBackground("splash.png"),
            [new CharacterInstance(characterId, new CharacterPlacement(new Point2D(60, 250), 120, Mirrored: true), RevisionOverride: null, new PoseData(ViewAngle.Front, [], []), Overrides: null)],
            [new Bubble(
                BubbleId.New(),
                BubbleStylePresets.GenerateShape(BubbleStylePreset.Speech, new Rect2D(20, 20, 120, 60)),
                BubbleStylePreset.Speech,
                [new BubbleTail(0.75, new Point2D(10, 120), TailKind.SmoothTriangle)],
                "Hello!")]);
        repository.SavePanel(issue.Id, page.Id, panel);

        Assert.Equivalent(issue, repository.LoadIssue(issue.Id), strict: true);
        Assert.Equivalent(page, repository.LoadPage(issue.Id, page.Id), strict: true);
        Assert.Equivalent(panel, repository.LoadPanel(issue.Id, page.Id, panel.Id), strict: true);

        var panelsDir = Path.Combine(_root, "issues", $"{issue.Id.Value}-1-issue-one", "pages", $"{pageId.Value}-splash", "panels");
        Assert.True(File.Exists(Path.Combine(panelsDir, $"{panelId.Value}.json")));
    }

    [Fact]
    public void Issue_page_numbering_round_trips_and_is_absent_when_unset()
    {
        var repository = ProjectRepository.Initialize(_root, "My Comic", new PageTrim(new PageSize(210, 297), 3));
        var plain = new Issue(IssueId.New(), "1", "", [], new SortedDictionary<CharacterId, CharacterRevisionId>());
        var numbered = new Issue(IssueId.New(), "2", "", [], new SortedDictionary<CharacterId, CharacterRevisionId>(),
            new PageNumbering(PageNumberPosition.BottomOuter, StartAt: 5, NumberFirstPage: true));

        repository.SaveIssue(plain);
        repository.SaveIssue(numbered);

        Assert.Null(repository.LoadIssue(plain.Id).PageNumbering);
        Assert.Equal(numbered.PageNumbering, repository.LoadIssue(numbered.Id).PageNumbering);
        var plainJson = File.ReadAllText(Directory.GetFiles(Path.Combine(_root, "issues"), "issue.json", SearchOption.AllDirectories)
            .Single(f => f.Contains(plain.Id.Value, StringComparison.Ordinal)));
        Assert.DoesNotContain("pageNumbering", plainJson, StringComparison.Ordinal);
        var numberedJson = File.ReadAllText(Directory.GetFiles(Path.Combine(_root, "issues"), "issue.json", SearchOption.AllDirectories)
            .Single(f => f.Contains(numbered.Id.Value, StringComparison.Ordinal)));
        Assert.Contains("\"bottomOuter\"", numberedJson, StringComparison.Ordinal);
    }

    [Fact]
    public void Loading_an_unknown_id_throws()
    {
        var repository = ProjectRepository.Initialize(_root, "My Comic", new PageTrim(new PageSize(210, 297), 3));
        Assert.Throws<DirectoryNotFoundException>(() => repository.LoadCharacter(CharacterId.New()));
        Assert.Throws<FileNotFoundException>(() => repository.LoadPose(PoseId.New()));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
