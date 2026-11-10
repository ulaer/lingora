using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Xml.Linq;
using Lingora.Core.Game;

namespace Lingora.Desktop;

/// <summary>
/// game-assets SVG'lerini WPF çizimine çevirir ve sahneleri preview.html'deki kurala göre birleştirir.
/// Stil kilidi yalnızca rect/circle/ellipse/path/polygon, düz dolgu, ince çizgi ve transform kullandığından
/// harici SVG kütüphanesi gerekmez.
/// </summary>
internal sealed class SvgScene
{
    private const double Size = 256;

    /// <summary>Elde birden çok nesne: kopyalar arası mesafe (nesne genişliğinin oranı). v3: 0.55 → 0.3, kopyalar avuçta toplanır.</summary>
    internal const double MultiSpacing = 0.3;
    private static readonly Regex TransformPart = new(@"(\w+)\s*\(([^)]*)\)", RegexOptions.Compiled);
    private static readonly Regex Number = new(@"-?\d*\.?\d+(?:[eE][-+]?\d+)?", RegexOptions.Compiled);
    private readonly GameLibrary _library;
    private readonly Dictionary<string, Parsed> _cache = new(StringComparer.Ordinal);

    public SvgScene(GameLibrary library) => _library = library;

    /// <summary>
    /// Nesnenin görünür oranı (0–1): nesne piksellerinden kaçı arkasındaki karakter/arka plandan farklı renkte.
    /// Ör. sarı tişörtün önündeki sarı elma neredeyse görünmez; oyun böyle sahneleri atlar. Nesne yoksa 1.
    /// </summary>
    public double ObjectVisibility(GameScene scene)
    {
        if (scene.Object is null || scene.Count == 0)
        {
            return 1;
        }

        var under = Pixels(Render(scene, includeBase: true, includeObject: false, frame: 0));
        var obj = Pixels(Render(scene, includeBase: false, includeObject: true, frame: 0));
        int total = 0, hidden = 0;
        for (var i = 0; i < obj.Length; i += 4)
        {
            if (obj[i + 3] < 250)
            {
                continue; // kenar yumuşatma pikselleri sayılmaz
            }

            total++;
            var same = Math.Abs(obj[i] - under[i]) + Math.Abs(obj[i + 1] - under[i + 1]) + Math.Abs(obj[i + 2] - under[i + 2]) < 24;
            if (same && under[i + 3] > 250)
            {
                hidden++;
            }
        }

        return total == 0 ? 0 : 1 - ((double)hidden / total);
    }

    private enum HoldMode
    {
        All,
        NoHolds,
        HoldsOnly,
    }

    private HoldMode _holdMode;

    /// <summary>Turun görünür oranı: legacy sahne eski yolla, kayıtta her aktörün elindeki nesnenin en düşüğü.</summary>
    public double ObjectVisibility(GameRound round)
    {
        if (round.Legacy is { } legacy)
        {
            return ObjectVisibility(legacy);
        }

        var record = round.Record;
        var worst = 1.0;
        for (var p = -1; p < record.Panels.Count; p++)
        {
            var actors = p < 0 ? record.Actors : record.Panels[p].Actors;
            for (var a = 0; a < actors.Count; a++)
            {
                if (actors[a].Hold is null || !HasAsset(actors[a].Hold!.Obj))
                {
                    continue;
                }

                GameActor[] Only() => actors.Select((x, i) => i == a ? x : x with { Hold = null }).ToArray();
                var variant = p < 0
                    ? record with { Actors = Only() }
                    : record with { Panels = record.Panels.Select((pn, i) => i == p ? pn with { Actors = Only() } : pn with { Actors = pn.Actors.Select(x => x with { Hold = null }).ToArray() }).ToArray() };
                worst = Math.Min(worst, HoldVisibility(variant));
            }
        }

        return worst;
    }

    private double HoldVisibility(GameRecord variant)
    {
        var width = (int)WidthOf(variant);
        _holdMode = HoldMode.NoHolds;
        var under = PixelsOf(Render(variant), width);
        _holdMode = HoldMode.HoldsOnly;
        var obj = PixelsOf(Render(variant), width);
        _holdMode = HoldMode.All;
        int total = 0, hidden = 0;
        for (var i = 0; i < obj.Length; i += 4)
        {
            if (obj[i + 3] < 250)
            {
                continue;
            }

            total++;
            var same = Math.Abs(obj[i] - under[i]) + Math.Abs(obj[i + 1] - under[i + 1]) + Math.Abs(obj[i + 2] - under[i + 2]) < 24;
            if (same && under[i + 3] > 250)
            {
                hidden++;
            }
        }

        return total == 0 ? 1 : 1 - ((double)hidden / total);
    }

    private static byte[] PixelsOf(DrawingImage image, int width)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(image, new Rect(0, 0, width, Size));
        }

        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(width, (int)Size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var pixels = new byte[width * (int)Size * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);
        return pixels;
    }

    private static byte[] Pixels(DrawingImage image)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(image, new Rect(0, 0, Size, Size));
        }

        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Size, (int)Size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var pixels = new byte[(int)Size * (int)Size * 4];
        bitmap.CopyPixels(pixels, (int)Size * 4, 0);
        return pixels;
    }

    /// <summary>Sahneyi 256×256 birimlik, kırpılmış bir çizim olarak döner.</summary>
    public DrawingImage Render(GameScene scene) => Render(scene, includeBase: true, includeObject: true, frame: 0);

    /// <summary>Sahneyi animasyonun <paramref name="frame"/>. adımında çizer (0 = statik, 1. kare).</summary>
    public DrawingImage Render(GameScene scene, int frame) => Render(scene, includeBase: true, includeObject: true, frame);

    private DrawingImage Render(GameScene scene, bool includeBase, bool includeObject, int frame)
    {
        var root = new DrawingGroup { ClipGeometry = new RectangleGeometry(new Rect(0, 0, Size, Size)) };

        // Saydam çerçeve: arka plansız sahnelerde de çizim 256×256 kalsın.
        root.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, Size, Size))));

        if (includeBase && scene.Background is { } background)
        {
            root.Children.Add(Asset(background, Matrix.Identity, new Style(null, null, frame)));
        }

        if (includeBase && scene.Group is { Count: > 0 } group)
        {
            foreach (var member in group)
            {
                var m = Matrix.Identity;
                m.Scale(member.Scale, member.Scale);
                m.Translate(member.X - (128 * member.Scale), 230 - (230 * member.Scale));
                root.Children.Add(Asset(member.Character, m, new Style(null, null, frame)));
            }
        }
        else if (includeBase)
        {
            root.Children.Add(Asset(scene.Character, Matrix.Identity, new Style(null, null, frame)));
        }

        if (includeObject && scene.Object is { } objectId)
        {
            var s = scene.Scale;
            for (var i = 0; i < scene.Count; i++)
            {
                // Nesnenin alt-orta noktası anchor'a oturur; çoğaltmada yan yana dizilir.
                var dx = scene.AnchorX - (128 * s) + ((i - ((scene.Count - 1) / 2.0)) * 256 * s * MultiSpacing);
                var m = Matrix.Identity;
                m.Scale(s, s);
                m.Translate(dx, scene.AnchorY - (230 * s));
                root.Children.Add(Asset(objectId, m, new Style(scene.Tint, null, frame)));
            }
        }

        root.Freeze();
        var image = new DrawingImage(root);
        image.Freeze();
        return image;
    }

    /// <summary>Tek bir asset'i (önizleme ve testler için).</summary>
    public DrawingImage RenderAsset(string assetId)
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, Size, Size))));
        group.Children.Add(Asset(assetId, Matrix.Identity, new Style(null, null, 0)));
        group.Freeze();
        return new DrawingImage(group);
    }

    /// <summary>Sahnede kare (data-frame) ya da durum geçişi var mı? Yoksa zamanlayıcıya gerek yoktur.</summary>
    public bool IsAnimated(GameRound round)
    {
        var ids = new List<string?>();
        if (round.Legacy is { } scene)
        {
            ids.Add(scene.Background);
            ids.Add(scene.Object);
            ids.AddRange(scene.Group is { Count: > 0 } g ? g.Select(m => m.Character) : [scene.Character]);
        }
        else
        {
            var record = round.Record;
            foreach (var v in new IGameVisual[] { record }.Concat(record.Panels))
            {
                ids.Add(v.Bg ?? record.Bg);
                ids.Add(v.SkyOverlay);
                ids.Add(v.Prop?.Obj);
                ids.Add(v.Bubble is null ? null : v.Bubble.Type + "_bubble");
                if (v.Weather is { } w && _library.Weathers.TryGetValue(w, out var weather))
                {
                    ids.Add(weather.Overlay);
                }

                foreach (var a in v.Actors)
                {
                    ids.Add(a.Char);
                    ids.Add(a.Hold?.Obj);
                    ids.Add(a.Marker);
                    if (a.Expr is not null && _library.Emotions.TryGetValue(a.Expr, out var e))
                    {
                        ids.Add(e.Mark);
                    }
                }
            }

            if (record.Prop is { States.Count: > 1 } && HasAsset(record.Prop.Obj) && Load(record.Prop.Obj).Shapes.Any(s => s.States.Length > 0))
            {
                return true;
            }
        }

        return ids.Where(HasAsset).Distinct().Any(id => Load(id!).Shapes.Any(s => s.Frames.Length > 0));
    }

    /// <summary>Panel kareleri arası boşluk (birim).</summary>
    internal const double PanelGap = 8;

    /// <summary>Kayıt (A2/B1/A1.jsonl) görselinin genişliği: panelli sahnede kare sayısına göre.</summary>
    internal static double WidthOf(GameRecord record) =>
        record.Layout == "panels" && record.Panels.Count > 0
            ? (record.Panels.Count * Size) + ((record.Panels.Count - 1) * PanelGap)
            : Size;

    /// <summary>Turun görselini çizer (A1 için scenes.json sahnesi, diğerleri jsonl kaydı).</summary>
    public DrawingImage Render(GameRound round, int frame = 0) =>
        round.Legacy is { } legacy ? Render(legacy, frame) : Render(round.Record, round.Sentence, frame);

    /// <summary>
    /// jsonl kaydını preview.html'deki kurala göre birleştirir: arka plan (gökyüzü tonu), gökyüzü işareti, durumlu nesne,
    /// hava, aktörler (nesne, ifade, işaret), balon, panelli sahnede kareler yan yana, sorulan cümlenin rozetleri.
    /// <paramref name="frame"/>: animasyon adımı (0 = statik).
    /// </summary>
    public DrawingImage Render(GameRecord record, GameSentence? sentence = null, int frame = 0)
    {
        var width = WidthOf(record);
        var negate = sentence?.Badges.Contains("badge_not") == true;
        var root = new DrawingGroup { ClipGeometry = new RectangleGeometry(new Rect(0, 0, width, Size)) };
        root.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, width, Size))));

        if (record.Layout == "panels" && record.Panels.Count > 0)
        {
            for (var i = 0; i < record.Panels.Count; i++)
            {
                var panel = Panel(record.Panels[i], record.Bg, frame, i == 0 && negate);
                var m = Matrix.Identity;
                m.Translate(i * (Size + PanelGap), 0);
                panel.Transform = new MatrixTransform(m);
                root.Children.Add(panel);
            }
        }
        else
        {
            root.Children.Add(Panel(record, record.Bg, frame, negate));
        }

        var badge = _library.Layouts.Badge;
        var badges = sentence?.Badges ?? [];
        for (var i = 0; i < badges.Count; i++)
        {
            if (HasAsset(badges[i]))
            {
                root.Children.Add(Asset(badges[i], Place(badge.X + (i * badge.Dx), badge.Y + (i * badge.Dy), badge.S), new Style(null, null, frame)));
            }
        }

        root.Freeze();
        var image = new DrawingImage(root);
        image.Freeze();
        return image;
    }

    private bool HasAsset(string? id) =>
        id is not null && _library.Assets.TryGetValue(id, out var a) && File.Exists(_library.AssetPath(a.Id));

    private static Matrix Place(double x, double y, double s)
    {
        var m = Matrix.Identity;
        m.Scale(s, s);
        m.Translate(x, y);
        return m;
    }

    private DrawingGroup Panel(IGameVisual v, string? bg, int frame, bool negate = false)
    {
        var L = _library.Layouts;
        var g = new DrawingGroup { ClipGeometry = new RectangleGeometry(new Rect(0, 0, Size, Size)) };
        g.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, Size, Size))));
        var plain = new Style(null, v.Sky, frame);

        var holdsOnly = _holdMode == HoldMode.HoldsOnly;
        if (!holdsOnly && HasAsset(bg))
        {
            g.Children.Add(Asset(bg!, Matrix.Identity, plain));
        }

        if (!holdsOnly && HasAsset(v.SkyOverlay))
        {
            g.Children.Add(Asset(v.SkyOverlay!, Place(L.SkyOverlay.X, L.SkyOverlay.Y, L.SkyOverlay.S), plain));
        }

        if (!holdsOnly && v.Prop is { } prop && HasAsset(prop.Obj))
        {
            if (v.Layout is "show_near" or "show_far")
            {
                // this/that: nesne tek başına; y alt kenardır (preview.html ile aynı), kopyalar 0.7 aralıkla.
                var box = v.Layout == "show_near" ? L.ShowNear : L.ShowFar;
                var ss = prop.Scale ?? box.S;
                for (var i = 0; i < prop.Count; i++)
                {
                    var x = box.X - (128 * ss) + ((i - ((prop.Count - 1) / 2.0)) * 256 * ss * 0.7);
                    g.Children.Add(Asset(prop.Obj, Place(x, box.Y - (230 * ss), ss), new Style(prop.Tint, null, frame)));
                }
            }
            else
            {
                var ps = prop.Scale ?? L.Prop.S;
                g.Children.Add(Asset(prop.Obj, Place(L.Prop.X - (128 * ps), 230 - (230 * ps), ps), new Style(null, null, frame, prop.States)));
            }
        }

        // Hava aktörlerin arkasında (yağmur/kar yüzü örtmesin).
        if (!holdsOnly && v.Weather is { } weatherId && _library.Weathers.TryGetValue(weatherId, out var weather) && HasAsset(weather.Overlay))
        {
            var placement = weather.Kind == "adj" && weather.Overlay == "sun" ? Place(190, 2, 0.25) : Matrix.Identity;
            g.Children.Add(Asset(weather.Overlay, placement, plain));
        }

        // Olumsuz cümle: çarpı, elinde nesne olan ilk aktörde (yoksa ilk aktörde).
        var crossActor = v.Actors.FirstOrDefault(a => a.Hold is not null) ?? v.Actors.FirstOrDefault();
        foreach (var actor in v.Actors)
        {
            g.Children.Add(Actor(actor, frame, negate && ReferenceEquals(actor, crossActor)));
        }

        if (!holdsOnly && v.Bubble is { } bubble && HasAsset(bubble.Type + "_bubble"))
        {
            var b = v.Layout == "pair_bubble" ? L.PairBubble : L.Bubble;
            g.Children.Add(Asset(bubble.Type + "_bubble", Place(b.X, b.Y, b.S), plain));
            var inside = bubble.ContentChar ?? bubble.ContentObj;
            if (HasAsset(inside))
            {
                var c = L.BubbleContent;
                g.Children.Add(Asset(inside!, Place(c.X - (128 * c.S), c.Y - (150 * c.S), c.S), plain));
            }
        }

        return g;
    }

    /// <summary>Aktör = karakter + elindeki nesne(ler) + yüz ifadesi + duygu işareti + ben/sen işareti (karakter koordinatlarında).</summary>
    private DrawingGroup Actor(GameActor a, int frame, bool negate = false)
    {
        var L = _library.Layouts;
        var group = new DrawingGroup { Transform = new MatrixTransform(Place(a.X - (128 * a.Scale), 230 - (230 * a.Scale), a.Scale)) };
        if (!HasAsset(a.Char))
        {
            return group;
        }

        var parsed = Load(a.Char);
        GameEmotion? emotion = null;
        if (a.Expr is not null)
        {
            _library.Emotions.TryGetValue(a.Expr, out emotion);
        }

        var hideEyes = emotion?.Eyes is not null && parsed.Parts.ContainsKey("eyes");
        var hideMouth = emotion?.Mouth is not null && parsed.Parts.ContainsKey("mouth");
        if (_holdMode != HoldMode.HoldsOnly)
        {
            group.Children.Add(Asset(a.Char, Matrix.Identity, new Style(null, null, frame, null, hideEyes, hideMouth)));
        }

        if (_holdMode != HoldMode.NoHolds && a.Hold is { } hold && HasAsset(hold.Obj))
        {
            var s = hold.Scale;
            for (var i = 0; i < hold.Count; i++)
            {
                var dx = hold.AnchorX - (128 * s) + ((i - ((hold.Count - 1) / 2.0)) * 256 * s * MultiSpacing);
                group.Children.Add(Asset(hold.Obj, Place(dx, hold.AnchorY - (230 * s), s), new Style(hold.Tint, null, frame)));
            }
        }

        if (_holdMode == HoldMode.HoldsOnly)
        {
            return group;
        }

        if (emotion is not null)
        {
            void Swap(string part, string? faceId)
            {
                if (faceId is null || !parsed.Parts.TryGetValue(part, out var p) || !HasAsset(faceId))
                {
                    return;
                }

                group.Children.Add(Asset(faceId, Place(p.X - 128, p.Y - 128, 1), new Style(null, null, frame)));
            }

            Swap("mouth", emotion.Mouth);
            Swap("eyes", emotion.Eyes);
            Swap("eyes", emotion.Brows);
            var head = parsed.Head ?? new Point(128, 40);
            if (HasAsset(emotion.Mark))
            {
                var m = L.Mark; // X/Y = baş noktasına göre kayma (dx, dy)
                group.Children.Add(Asset(emotion.Mark!, Place(head.X + m.X - (128 * m.S), head.Y + m.Y - (128 * m.S), m.S), new Style(null, null, frame)));
            }
        }

        if (HasAsset(a.Marker))
        {
            var head = parsed.Head ?? new Point(128, 40);
            var m = L.Marker; // Y = dy: işaret merkezi (head.x, head.y + dy)
            var center = MarkerCenter(head, m, a.Scale);
            group.Children.Add(Asset(a.Marker!, Place(center.X - (128 * m.S), center.Y - (128 * m.S), m.S), new Style(null, null, frame)));
        }

        if (negate && HasAsset("badge_not"))
        {
            // Çarpı: elde nesne varsa tutma noktasında, yoksa göğüste (data-head + (0, 70)).
            var head = parsed.Head ?? new Point(128, 40);
            var at = a.Hold is { } h ? new Point(h.AnchorX, h.AnchorY) : new Point(head.X, head.Y + 70);
            group.Children.Add(Asset("badge_not", Place(at.X - (128 * CrossScale), at.Y - (128 * CrossScale), CrossScale), new Style(null, null, frame)));
        }

        return group;
    }

    /// <summary>Olumsuz cümlede inkâr edilen şeyin üstündeki çarpının ölçeği.</summary>
    internal const double CrossScale = 0.12;

    /// <summary>Ben/sen işaretinin yarı yüksekliği (≈120 px'lik şekil / 256 kutu).</summary>
    private const double MarkerHalfHeight = 60;

    /// <summary>
    /// İşaret merkezi (aktör koordinatlarında): önce başın üstü (head + (0, dy)); tuvaldeki üst kenarı 4'ün altına
    /// düşüyorsa başın yanına (head + (52, 24)).
    /// </summary>
    internal static Point MarkerCenter(Point head, GamePlacement marker, double actorScale)
    {
        var above = new Point(head.X, head.Y + marker.Y);
        var canvasTop = 230 - (230 * actorScale) + (actorScale * (above.Y - (MarkerHalfHeight * marker.S)));
        return canvasTop < 4 ? new Point(head.X + 52, head.Y + 24) : above;
    }

    private Parsed Load(string assetId)
    {
        if (!_cache.TryGetValue(assetId, out var parsed))
        {
            parsed = Parse(_library.AssetPath(assetId));
            _cache[assetId] = parsed;
        }

        return parsed;
    }

    private static bool Visible(Shape shape, Style style)
    {
        foreach (var f in shape.Frames)
        {
            if (f.Index != style.Frame % f.Count)
            {
                return false;
            }
        }

        foreach (var c in shape.States)
        {
            // Durum listesi yalnızca nesnenin kök grubundaki durumları sürer; diğerleri ilk durumda kalır.
            var current = style.States is { Count: > 0 } list && c.OnRoot ? list[style.Frame / 3 % list.Count] : c.First;
            if (c.State != current)
            {
                return false;
            }
        }

        return !((style.HideEyes && shape.Part == "eyes") || (style.HideMouth && shape.Part == "mouth"));
    }

    private DrawingGroup Asset(string assetId, Matrix placement, Style style)
    {
        var shapes = Load(assetId).Shapes;
        var group = new DrawingGroup { Transform = new MatrixTransform(placement) };
        Brush? primary = style.Primary is null ? null : Brush(style.Primary);
        Brush? sky = style.Sky is null ? null : Brush(style.Sky);
        foreach (var shape in shapes)
        {
            if (!Visible(shape, style))
            {
                continue;
            }

            var fill = shape.Tint switch
            {
                TintKind.Primary when primary is not null => primary,
                TintKind.Sky when sky is not null => sky,
                _ => shape.Fill,
            };
            var drawing = new GeometryDrawing(fill, shape.Pen, shape.Geometry);
            if (shape.Transform.IsIdentity)
            {
                group.Children.Add(drawing);
            }
            else
            {
                var wrapper = new DrawingGroup { Transform = new MatrixTransform(shape.Transform) };
                wrapper.Children.Add(drawing);
                group.Children.Add(wrapper);
            }
        }

        return group;
    }

    /// <summary>
    /// SVG dosyasını düz bir şekil listesine çevirir (iç içe g transformları birleştirilir). Her şekil hangi
    /// animasyon karesine / duruma / yüz parçasına ait olduğunu taşır; hangisinin çizileceği çizim anında seçilir.
    /// </summary>
    internal static Parsed Parse(string path)
    {
        var doc = XDocument.Load(path);
        var shapes = new List<Shape>();
        var rootG = doc.Root!.Elements().FirstOrDefault(e => e.Name.LocalName == "g");
        var parts = new Dictionary<string, Point>(StringComparer.Ordinal);
        Point? head = null;
        if (rootG is not null && (string?)rootG.Attribute("data-head") is { } headText)
        {
            var n = Number.Matches(headText).Select(m => double.Parse(m.Value, CultureInfo.InvariantCulture)).ToArray();
            if (n.Length >= 2)
            {
                head = new Point(n[0], n[1]);
            }
        }

        Walk(doc.Root!, Matrix.Identity, shapes, [], [], null, rootG, parts);
        return new Parsed(shapes, head, parts);
    }

    private static string? Inherited(XElement element, string attribute) =>
        element.AncestorsAndSelf().Select(a => (string?)a.Attribute(attribute)).FirstOrDefault(v => v is not null);

    private static void Walk(
        XElement element,
        Matrix parent,
        List<Shape> shapes,
        FrameCond[] frames,
        StateCond[] states,
        string? part,
        XElement? rootG,
        Dictionary<string, Point> parts)
    {
        foreach (var child in element.Elements())
        {
            var local = ParseTransform((string?)child.Attribute("transform"));
            var matrix = Matrix.Multiply(local, parent);
            var name = child.Name.LocalName;
            var childPart = (string?)child.Attribute("data-part") ?? part;
            if (name == "g")
            {
                var childFrames = frames;
                if (int.TryParse((string?)child.Attribute("data-frame"), out var frameNo))
                {
                    var count = element.Elements().Count(e => e.Attribute("data-frame") is not null);
                    childFrames = [.. frames, new FrameCond(element, frameNo - 1, count)];
                }

                var childStates = states;
                if ((string?)child.Attribute("data-state") is { } state)
                {
                    var first = (string?)element.Elements().First(e => e.Attribute("data-state") is not null).Attribute("data-state") ?? state;
                    childStates = [.. states, new StateCond(element, state, first, element == rootG)];
                }

                if ((string?)child.Attribute("data-part") is { } partName
                    && double.TryParse((string?)child.Attribute("data-cx"), NumberStyles.Float, CultureInfo.InvariantCulture, out var cx)
                    && double.TryParse((string?)child.Attribute("data-cy"), NumberStyles.Float, CultureInfo.InvariantCulture, out var cy))
                {
                    parts[partName] = new Point(cx, cy);
                }

                Walk(child, matrix, shapes, childFrames, childStates, childPart, rootG, parts);
                continue;
            }

            var geometry = Geometry(child, name);
            if (geometry is null)
            {
                continue;
            }

            // fill/stroke grup üzerinde de olabilir (SVG mirası); en yakın atadaki değer geçerli.
            var fillText = Inherited(child, "fill");
            var fill = fillText is null ? Brushes.Black : fillText == "none" ? null : Brush(fillText);
            Pen? pen = null;
            if (Inherited(child, "stroke") is { } stroke && stroke != "none")
            {
                var width = Inherited(child, "stroke-width") is { } w
                    ? double.Parse(w, CultureInfo.InvariantCulture)
                    : 1;
                pen = new Pen(Brush(stroke), width);
                if (Inherited(child, "stroke-linecap") == "round")
                {
                    pen.StartLineCap = pen.EndLineCap = PenLineCap.Round;
                }

                pen.LineJoin = PenLineJoin.Round;
                pen.Freeze();
            }

            // Tint: şeklin kendi data-tint'i ya da (kendi fill'i yoksa) atadaki grubun data-tint'i.
            var tintText = (string?)child.Attribute("data-tint")
                           ?? (child.Attribute("fill") is null ? child.Ancestors().Select(a => (string?)a.Attribute("data-tint")).FirstOrDefault(v => v is not null) : null);
            var tint = tintText switch { "primary" => TintKind.Primary, "sky" => TintKind.Sky, _ => TintKind.None };
            shapes.Add(new Shape(geometry, fill, pen, tint, matrix, childPart, frames, states));
        }
    }

    private static Geometry? Geometry(XElement e, string name)
    {
        Geometry? geometry = name switch
        {
            "rect" => Rect(e),
            "circle" => new EllipseGeometry(new Point(D(e, "cx"), D(e, "cy")), D(e, "r"), D(e, "r")),
            "ellipse" => new EllipseGeometry(new Point(D(e, "cx"), D(e, "cy")), D(e, "rx"), D(e, "ry")),
            "path" => PathGeometry(e),
            "polygon" => Polygon((string?)e.Attribute("points") ?? string.Empty),
            _ => null,
        };
        geometry?.Freeze();
        return geometry;
    }

    private static Geometry Rect(XElement e)
    {
        var rx = e.Attribute("rx") is null ? (e.Attribute("ry") is null ? 0 : D(e, "ry")) : D(e, "rx");
        var ry = e.Attribute("ry") is null ? rx : D(e, "ry");
        return new RectangleGeometry(new Rect(D(e, "x"), D(e, "y"), D(e, "width"), D(e, "height")), rx, ry);
    }

    private static Geometry PathGeometry(XElement e)
    {
        // SVG varsayılanı nonzero; WPF yol dilinde F1 = nonzero, F0 = evenodd.
        var rule = (string?)e.Attribute("fill-rule") == "evenodd" ? "F0 " : "F1 ";
        return System.Windows.Media.Geometry.Parse(rule + ((string?)e.Attribute("d") ?? string.Empty));
    }

    private static Geometry Polygon(string points)
    {
        var values = Number.Matches(points).Select(m => double.Parse(m.Value, CultureInfo.InvariantCulture)).ToList();
        var geometry = new StreamGeometry { FillRule = FillRule.Nonzero };
        using (var ctx = geometry.Open())
        {
            if (values.Count >= 4)
            {
                ctx.BeginFigure(new Point(values[0], values[1]), isFilled: true, isClosed: true);
                for (var i = 2; i + 1 < values.Count; i += 2)
                {
                    ctx.LineTo(new Point(values[i], values[i + 1]), isStroked: true, isSmoothJoin: false);
                }
            }
        }

        return geometry;
    }

    internal static Matrix ParseTransform(string? text)
    {
        var result = Matrix.Identity;
        if (string.IsNullOrWhiteSpace(text))
        {
            return result;
        }

        foreach (Match part in TransformPart.Matches(text))
        {
            var n = Number.Matches(part.Groups[2].Value).Select(m => double.Parse(m.Value, CultureInfo.InvariantCulture)).ToArray();
            var t = Matrix.Identity;
            switch (part.Groups[1].Value)
            {
                case "translate":
                    t.Translate(n.ElementAtOrDefault(0), n.ElementAtOrDefault(1));
                    break;
                case "scale":
                    t.Scale(n.ElementAtOrDefault(0), n.Length > 1 ? n[1] : n.ElementAtOrDefault(0));
                    break;
                case "rotate":
                    if (n.Length >= 3)
                    {
                        t.RotateAt(n[0], n[1], n[2]);
                    }
                    else
                    {
                        t.Rotate(n.ElementAtOrDefault(0));
                    }

                    break;
                case "matrix" when n.Length == 6:
                    t = new Matrix(n[0], n[1], n[2], n[3], n[4], n[5]);
                    break;
            }

            // SVG'de "A B": önce B uygulanır. WPF satır vektörü: p * B * A.
            result = Matrix.Multiply(t, result);
        }

        return result;
    }

    private static double D(XElement e, string name, double fallback = 0) =>
        double.TryParse((string?)e.Attribute(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    private static SolidColorBrush Brush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    internal enum TintKind
    {
        None,
        Primary,
        Sky,
    }

    internal sealed record FrameCond(object Group, int Index, int Count);

    internal sealed record StateCond(object Group, string State, string First, bool OnRoot);

    internal sealed record Shape(Geometry Geometry, Brush? Fill, Pen? Pen, TintKind Tint, Matrix Transform, string? Part, FrameCond[] Frames, StateCond[] States);

    internal sealed record Parsed(List<Shape> Shapes, Point? Head, Dictionary<string, Point> Parts);

    /// <summary>Çizim bağlamı: tint renkleri, animasyon karesi, durum dizisi, gizlenecek yüz parçaları.</summary>
    internal readonly record struct Style(string? Primary, string? Sky, int Frame, IReadOnlyList<string>? States = null, bool HideEyes = false, bool HideMouth = false);
}
