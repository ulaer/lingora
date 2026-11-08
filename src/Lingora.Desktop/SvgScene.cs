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
    private static readonly Regex TransformPart = new(@"(\w+)\s*\(([^)]*)\)", RegexOptions.Compiled);
    private static readonly Regex Number = new(@"-?\d*\.?\d+(?:[eE][-+]?\d+)?", RegexOptions.Compiled);
    private readonly GameLibrary _library;
    private readonly Dictionary<string, List<Shape>> _cache = new(StringComparer.Ordinal);

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

        var under = Pixels(Render(scene, includeBase: true, includeObject: false));
        var obj = Pixels(Render(scene, includeBase: false, includeObject: true));
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
    public DrawingImage Render(GameScene scene) => Render(scene, includeBase: true, includeObject: true);

    private DrawingImage Render(GameScene scene, bool includeBase, bool includeObject)
    {
        var root = new DrawingGroup { ClipGeometry = new RectangleGeometry(new Rect(0, 0, Size, Size)) };

        // Saydam çerçeve: arka plansız sahnelerde de çizim 256×256 kalsın.
        root.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, Size, Size))));

        if (includeBase && scene.Background is { } background)
        {
            root.Children.Add(Asset(background, null, Matrix.Identity));
        }

        if (includeBase && scene.Group is { Count: > 0 } group)
        {
            foreach (var member in group)
            {
                var m = Matrix.Identity;
                m.Scale(member.Scale, member.Scale);
                m.Translate(member.X - (128 * member.Scale), 230 - (230 * member.Scale));
                root.Children.Add(Asset(member.Character, null, m));
            }
        }
        else if (includeBase)
        {
            root.Children.Add(Asset(scene.Character, null, Matrix.Identity));
        }

        if (includeObject && scene.Object is { } objectId)
        {
            var s = scene.Scale;
            for (var i = 0; i < scene.Count; i++)
            {
                // Nesnenin alt-orta noktası anchor'a oturur; çoğaltmada yan yana dizilir.
                var dx = scene.AnchorX - (128 * s) + ((i - ((scene.Count - 1) / 2.0)) * 256 * s * 0.55);
                var m = Matrix.Identity;
                m.Scale(s, s);
                m.Translate(dx, scene.AnchorY - (230 * s));
                root.Children.Add(Asset(objectId, scene.Tint, m));
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
        group.Children.Add(Asset(assetId, null, Matrix.Identity));
        group.Freeze();
        return new DrawingImage(group);
    }

    private DrawingGroup Asset(string assetId, string? tint, Matrix placement)
    {
        if (!_cache.TryGetValue(assetId, out var shapes))
        {
            shapes = Parse(_library.AssetPath(assetId));
            _cache[assetId] = shapes;
        }

        var group = new DrawingGroup { Transform = new MatrixTransform(placement) };
        Brush? tintBrush = tint is null ? null : Brush(tint);
        foreach (var shape in shapes)
        {
            var fill = shape.Tint && tintBrush is not null ? tintBrush : shape.Fill;
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

    /// <summary>SVG dosyasını düz bir şekil listesine çevirir (iç içe g transformları birleştirilir).</summary>
    internal static List<Shape> Parse(string path)
    {
        var doc = XDocument.Load(path);
        var shapes = new List<Shape>();
        Walk(doc.Root!, Matrix.Identity, shapes);
        return shapes;
    }

    private static void Walk(XElement element, Matrix parent, List<Shape> shapes)
    {
        foreach (var child in element.Elements())
        {
            var local = ParseTransform((string?)child.Attribute("transform"));
            var matrix = Matrix.Multiply(local, parent);
            var name = child.Name.LocalName;
            if (name == "g")
            {
                Walk(child, matrix, shapes);
                continue;
            }

            var geometry = Geometry(child, name);
            if (geometry is null)
            {
                continue;
            }

            var fillText = (string?)child.Attribute("fill");
            var fill = fillText is null ? Brushes.Black : fillText == "none" ? null : Brush(fillText);
            Pen? pen = null;
            if ((string?)child.Attribute("stroke") is { } stroke && stroke != "none")
            {
                pen = new Pen(Brush(stroke), D(child, "stroke-width", 1));
                if ((string?)child.Attribute("stroke-linecap") == "round")
                {
                    pen.StartLineCap = pen.EndLineCap = PenLineCap.Round;
                }

                pen.LineJoin = PenLineJoin.Round;
                pen.Freeze();
            }

            shapes.Add(new Shape(geometry, fill, pen, (string?)child.Attribute("data-tint") == "primary", matrix));
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

    internal sealed record Shape(Geometry Geometry, Brush? Fill, Pen? Pen, bool Tint, Matrix Transform);
}
