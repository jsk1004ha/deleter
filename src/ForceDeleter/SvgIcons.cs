using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Reflection;
using System.Xml.Linq;

namespace ForceDeleter;

/// <summary>Renders the bundled Tabler SVG icons without external runtime dependencies.</summary>
public static class SvgIcons
{
    private const string ResourcePrefix = "ForceDeleter.Assets.Icons.";

    private static readonly HashSet<string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        "trash", "folder", "clock", "settings", "file", "photo", "file-zip", "shield",
        "alert-triangle", "lock", "moon", "search", "x", "check", "chevron-down",
        "chevron-right", "download", "upload", "device-floppy", "plus", "minus", "square",
        "dots", "folder-up", "folder-filled", "file-filled", "circle-plus-filled",
    };

    private static readonly ConcurrentDictionary<string, Lazy<SvgIcon>> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    public static void Draw(Graphics g, Rectangle bounds, string name, Color color, float penWidth = 1.8f)
    {
        ArgumentNullException.ThrowIfNull(g);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }
        if (!float.IsFinite(penWidth) || penWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(penWidth), "Pen width must be a positive finite number.");
        }

        string key = name.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
        if (!Names.Contains(key))
        {
            throw new ArgumentException($"Unknown SVG icon '{name}'.", nameof(name));
        }

        SvgIcon icon = Cache.GetOrAdd(key, static iconName => new Lazy<SvgIcon>(
            () => Load(iconName), LazyThreadSafetyMode.ExecutionAndPublication)).Value;
        float scale = Math.Min(bounds.Width / icon.Width, bounds.Height / icon.Height);
        float x = bounds.X + (bounds.Width - icon.Width * scale) / 2f;
        float y = bounds.Y + (bounds.Height - icon.Height * scale) / 2f;

        GraphicsState state = g.Save();
        try
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var transform = new Matrix(scale, 0, 0, scale,
                x - icon.MinX * scale, y - icon.MinY * scale);
            g.MultiplyTransform(transform, MatrixOrder.Prepend);

            using var brush = new SolidBrush(color);
            using var pen = new Pen(color, penWidth)
            {
                StartCap = icon.LineCap,
                EndCap = icon.LineCap,
                DashCap = icon.LineCap == LineCap.Round ? DashCap.Round : DashCap.Flat,
                LineJoin = icon.LineJoin,
                MiterLimit = icon.MiterLimit,
            };

            foreach (SvgShape shape in icon.Shapes)
            {
                if (shape.Fill) g.FillPath(brush, shape.Path);
                if (shape.Stroke) g.DrawPath(pen, shape.Path);
            }
        }
        finally
        {
            g.Restore(state);
        }
    }

    private static SvgIcon Load(string name)
    {
        string resourceName = ResourcePrefix + name + ".svg";
        Assembly assembly = typeof(SvgIcons).Assembly;
        using Stream stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded SVG resource '{resourceName}' was not found.");

        try
        {
            XDocument document = XDocument.Load(stream, LoadOptions.None);
            XElement root = document.Root ?? throw new InvalidDataException("The SVG has no root element.");
            if (root.Name.LocalName != "svg")
            {
                throw new InvalidDataException($"Expected <svg>, found <{root.Name.LocalName}>.");
            }
            RejectUnsupported(root, "transform", "clip-path", "mask", "filter", "style");

            float[] viewBox = ParseNumberList(Required(root, "viewBox"));
            if (viewBox.Length != 4 || viewBox[2] <= 0 || viewBox[3] <= 0)
            {
                throw new InvalidDataException("SVG viewBox must contain four numbers with positive dimensions.");
            }

            SvgPaint inherited = new(
                ParsePaint(root.Attribute("fill")?.Value ?? "black"),
                ParsePaint(root.Attribute("stroke")?.Value ?? "none"));
            var shapes = new List<SvgShape>();
            ParseChildren(root, inherited, shapes, name);
            if (shapes.Count == 0) throw new InvalidDataException("SVG contains no renderable shapes.");

            return new SvgIcon(
                viewBox[0], viewBox[1], viewBox[2], viewBox[3],
                ParseLineCap(root.Attribute("stroke-linecap")?.Value),
                ParseLineJoin(root.Attribute("stroke-linejoin")?.Value),
                Number(root, "stroke-miterlimit", 4f), shapes);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidDataException($"Unable to parse bundled SVG asset '{name}.svg': {ex.Message}", ex);
        }
    }

    private static void ParseChildren(XElement parent, SvgPaint inherited, List<SvgShape> shapes, string asset)
    {
        foreach (XElement element in parent.Elements())
        {
            RejectUnsupported(element, "transform", "clip-path", "mask", "filter", "style");
            SvgPaint paint = new(
                element.Attribute("fill") is XAttribute fill ? ParsePaint(fill.Value) : inherited.Fill,
                element.Attribute("stroke") is XAttribute stroke ? ParsePaint(stroke.Value) : inherited.Stroke);
            string kind = element.Name.LocalName;
            if (kind == "g")
            {
                ParseChildren(element, paint, shapes, asset);
                continue;
            }

            GraphicsPath path = kind switch
            {
                "path" => ParsePath(element),
                "line" => ParseLine(element),
                "polyline" => ParsePoly(element, false),
                "polygon" => ParsePoly(element, true),
                "rect" => ParseRect(element),
                "circle" => ParseCircle(element),
                "ellipse" => ParseEllipse(element),
                _ => throw new NotSupportedException($"SVG element <{kind}> in '{asset}.svg' is not supported."),
            };
            shapes.Add(new SvgShape(path, paint.Fill, paint.Stroke));
        }
    }

    private static GraphicsPath ParsePath(XElement element)
    {
        GraphicsPath path = new SvgPathParser(Required(element, "d")).Parse();
        path.FillMode = element.Attribute("fill-rule")?.Value.Trim() switch
        {
            null or "" or "nonzero" => FillMode.Winding,
            "evenodd" => FillMode.Alternate,
            var value => throw new NotSupportedException($"Unsupported SVG fill-rule '{value}'."),
        };
        return path;
    }

    private static GraphicsPath ParseLine(XElement e)
    {
        var path = new GraphicsPath();
        path.AddLine(Number(e, "x1"), Number(e, "y1"), Number(e, "x2"), Number(e, "y2"));
        return path;
    }

    private static GraphicsPath ParsePoly(XElement e, bool close)
    {
        float[] values = ParseNumberList(Required(e, "points"));
        if (values.Length < 4 || values.Length % 2 != 0)
            throw new InvalidDataException("SVG points must contain at least two coordinate pairs.");
        var points = new PointF[values.Length / 2];
        for (int i = 0; i < points.Length; i++) points[i] = new(values[i * 2], values[i * 2 + 1]);
        var path = new GraphicsPath();
        path.AddLines(points);
        if (close) path.CloseFigure();
        return path;
    }

    private static GraphicsPath ParseRect(XElement e)
    {
        float x = Number(e, "x"), y = Number(e, "y");
        float width = Number(e, "width"), height = Number(e, "height");
        if (width < 0 || height < 0) throw new InvalidDataException("SVG rect dimensions cannot be negative.");
        float rx = Math.Min(Math.Abs(Number(e, "rx")), width / 2f);
        float ry = Math.Min(Math.Abs(Number(e, "ry", rx)), height / 2f);
        var path = new GraphicsPath();
        if (rx == 0 || ry == 0)
        {
            path.AddRectangle(new RectangleF(x, y, width, height));
            return path;
        }
        path.AddArc(x, y, rx * 2, ry * 2, 180, 90);
        path.AddArc(x + width - rx * 2, y, rx * 2, ry * 2, 270, 90);
        path.AddArc(x + width - rx * 2, y + height - ry * 2, rx * 2, ry * 2, 0, 90);
        path.AddArc(x, y + height - ry * 2, rx * 2, ry * 2, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static GraphicsPath ParseCircle(XElement e)
    {
        float cx = Number(e, "cx"), cy = Number(e, "cy"), r = Number(e, "r");
        if (r < 0) throw new InvalidDataException("SVG circle radius cannot be negative.");
        var path = new GraphicsPath();
        path.AddEllipse(cx - r, cy - r, r * 2, r * 2);
        return path;
    }

    private static GraphicsPath ParseEllipse(XElement e)
    {
        float cx = Number(e, "cx"), cy = Number(e, "cy");
        float rx = Number(e, "rx"), ry = Number(e, "ry");
        if (rx < 0 || ry < 0) throw new InvalidDataException("SVG ellipse radii cannot be negative.");
        var path = new GraphicsPath();
        path.AddEllipse(cx - rx, cy - ry, rx * 2, ry * 2);
        return path;
    }

    private static bool ParsePaint(string value) => value.Trim() switch
    {
        "none" => false,
        "currentColor" => true,
        _ => throw new NotSupportedException($"Unsupported SVG paint '{value}'; expected 'none' or 'currentColor'."),
    };

    private static LineCap ParseLineCap(string? value) => value?.Trim() switch
    {
        null or "" or "butt" => LineCap.Flat,
        "round" => LineCap.Round,
        "square" => LineCap.Square,
        _ => throw new NotSupportedException($"Unsupported SVG stroke-linecap '{value}'."),
    };

    private static LineJoin ParseLineJoin(string? value) => value?.Trim() switch
    {
        null or "" or "miter" => LineJoin.Miter,
        "round" => LineJoin.Round,
        "bevel" => LineJoin.Bevel,
        _ => throw new NotSupportedException($"Unsupported SVG stroke-linejoin '{value}'."),
    };

    private static void RejectUnsupported(XElement element, params string[] names)
    {
        foreach (string name in names)
            if (element.Attribute(name) is not null)
                throw new NotSupportedException($"SVG attribute '{name}' on <{element.Name.LocalName}> is not supported.");
    }

    private static string Required(XElement element, string name) =>
        element.Attribute(name)?.Value
        ?? throw new InvalidDataException($"SVG <{element.Name.LocalName}> is missing required attribute '{name}'.");

    private static float Number(XElement element, string name, float fallback = 0)
    {
        string? text = element.Attribute(name)?.Value;
        if (text is null) return fallback;
        if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) || !float.IsFinite(value))
            throw new InvalidDataException($"SVG attribute '{name}' has invalid number '{text}'.");
        return value;
    }

    private static float[] ParseNumberList(string text)
    {
        var reader = new NumberReader(text);
        var values = new List<float>();
        while (reader.HasNumber) values.Add(reader.Read());
        reader.RequireEnd();
        return values.ToArray();
    }

    private sealed record SvgIcon(float MinX, float MinY, float Width, float Height,
        LineCap LineCap, LineJoin LineJoin, float MiterLimit, IReadOnlyList<SvgShape> Shapes);
    private sealed record SvgShape(GraphicsPath Path, bool Fill, bool Stroke);
    private readonly record struct SvgPaint(bool Fill, bool Stroke);

    private sealed class SvgPathParser
    {
        private readonly NumberReader _reader;
        private readonly GraphicsPath _path = new();
        private PointF _current;
        private PointF _start;
        private PointF? _lastCubic;
        private PointF? _lastQuadratic;

        public SvgPathParser(string data) => _reader = new(data);

        public GraphicsPath Parse()
        {
            char command = '\0';
            while (!_reader.End)
            {
                if (_reader.TryReadCommand(out char next)) command = next;
                else if (command == '\0' || command is 'Z' or 'z') throw _reader.Error("Expected an SVG path command.");

                bool relative = char.IsLower(command);
                switch (char.ToUpperInvariant(command))
                {
                    case 'M': Move(relative); command = relative ? 'l' : 'L'; break;
                    case 'L': Line(relative); break;
                    case 'H': Horizontal(relative); break;
                    case 'V': Vertical(relative); break;
                    case 'C': Cubic(relative); break;
                    case 'S': SmoothCubic(relative); break;
                    case 'Q': Quadratic(relative); break;
                    case 'T': SmoothQuadratic(relative); break;
                    case 'A': Arc(relative); break;
                    case 'Z': Close(); break;
                    default: throw _reader.Error($"Unsupported SVG path command '{command}'.");
                }
            }
            return _path;
        }

        private void Move(bool relative)
        {
            _path.StartFigure();
            _current = _start = ReadPoint(relative);
            ResetControls();
        }

        private void Line(bool relative) => LineTo(ReadPoint(relative));
        private void Horizontal(bool relative)
        {
            float x = _reader.Read() + (relative ? _current.X : 0);
            LineTo(new(x, _current.Y));
        }
        private void Vertical(bool relative)
        {
            float y = _reader.Read() + (relative ? _current.Y : 0);
            LineTo(new(_current.X, y));
        }

        private void Cubic(bool relative)
        {
            PointF c1 = ReadPoint(relative), c2 = ReadPoint(relative), end = ReadPoint(relative);
            _path.AddBezier(_current, c1, c2, end);
            _current = end; _lastCubic = c2; _lastQuadratic = null;
        }

        private void SmoothCubic(bool relative)
        {
            PointF c1 = _lastCubic is PointF last ? Reflect(last) : _current;
            PointF c2 = ReadPoint(relative), end = ReadPoint(relative);
            _path.AddBezier(_current, c1, c2, end);
            _current = end; _lastCubic = c2; _lastQuadratic = null;
        }

        private void Quadratic(bool relative) => AddQuadratic(ReadPoint(relative), ReadPoint(relative));
        private void SmoothQuadratic(bool relative) =>
            AddQuadratic(_lastQuadratic is PointF last ? Reflect(last) : _current, ReadPoint(relative));

        private void AddQuadratic(PointF control, PointF end)
        {
            PointF c1 = new(_current.X + 2f / 3f * (control.X - _current.X), _current.Y + 2f / 3f * (control.Y - _current.Y));
            PointF c2 = new(end.X + 2f / 3f * (control.X - end.X), end.Y + 2f / 3f * (control.Y - end.Y));
            _path.AddBezier(_current, c1, c2, end);
            _current = end; _lastQuadratic = control; _lastCubic = null;
        }

        private void Arc(bool relative)
        {
            float rx = Math.Abs(_reader.Read()), ry = Math.Abs(_reader.Read()), rotation = _reader.Read();
            bool large = ReadFlag(), sweep = ReadFlag();
            PointF end = ReadPoint(relative);
            AddArc(rx, ry, rotation, large, sweep, end);
            _current = end; ResetControls();
        }

        private bool ReadFlag() => _reader.Read() switch
        {
            0 => false,
            1 => true,
            _ => throw _reader.Error("SVG arc flags must be 0 or 1."),
        };

        private void AddArc(float rx, float ry, float rotation, bool large, bool sweep, PointF end)
        {
            if (rx == 0 || ry == 0 || Same(_current, end))
            {
                if (!Same(_current, end)) _path.AddLine(_current, end);
                return;
            }

            double phi = rotation * Math.PI / 180.0, cos = Math.Cos(phi), sin = Math.Sin(phi);
            double dx = (_current.X - end.X) / 2.0, dy = (_current.Y - end.Y) / 2.0;
            double xp = cos * dx + sin * dy, yp = -sin * dx + cos * dy;
            double rx2 = rx * rx, ry2 = ry * ry;
            double lambda = xp * xp / rx2 + yp * yp / ry2;
            if (lambda > 1)
            {
                double factor = Math.Sqrt(lambda);
                rx *= (float)factor; ry *= (float)factor; rx2 = rx * rx; ry2 = ry * ry;
            }
            double numerator = Math.Max(0, rx2 * ry2 - rx2 * yp * yp - ry2 * xp * xp);
            double denominator = rx2 * yp * yp + ry2 * xp * xp;
            double coefficient = denominator == 0 ? 0 : Math.Sqrt(numerator / denominator);
            if (large == sweep) coefficient = -coefficient;
            double cxp = coefficient * rx * yp / ry, cyp = coefficient * -ry * xp / rx;
            double cx = cos * cxp - sin * cyp + (_current.X + end.X) / 2.0;
            double cy = sin * cxp + cos * cyp + (_current.Y + end.Y) / 2.0;
            double ux = (xp - cxp) / rx, uy = (yp - cyp) / ry;
            double vx = (-xp - cxp) / rx, vy = (-yp - cyp) / ry;
            double startAngle = Math.Atan2(uy, ux);
            double sweepAngle = Math.Atan2(ux * vy - uy * vx, ux * vx + uy * vy);
            if (!sweep && sweepAngle > 0) sweepAngle -= 2 * Math.PI;
            if (sweep && sweepAngle < 0) sweepAngle += 2 * Math.PI;

            int segments = Math.Max(1, (int)Math.Ceiling(Math.Abs(sweepAngle) / (Math.PI / 2)));
            double delta = sweepAngle / segments;
            PointF start = _current;
            for (int i = 0; i < segments; i++)
            {
                double a1 = startAngle + i * delta, a2 = a1 + delta;
                double alpha = 4.0 / 3.0 * Math.Tan((a2 - a1) / 4.0);
                PointF c1 = ArcPoint(cx, cy, rx, ry, cos, sin, Math.Cos(a1) - alpha * Math.Sin(a1), Math.Sin(a1) + alpha * Math.Cos(a1));
                PointF c2 = ArcPoint(cx, cy, rx, ry, cos, sin, Math.Cos(a2) + alpha * Math.Sin(a2), Math.Sin(a2) - alpha * Math.Cos(a2));
                PointF finish = i == segments - 1 ? end : ArcPoint(cx, cy, rx, ry, cos, sin, Math.Cos(a2), Math.Sin(a2));
                _path.AddBezier(start, c1, c2, finish);
                start = finish;
            }
        }

        private static PointF ArcPoint(double cx, double cy, float rx, float ry, double cos, double sin, double x, double y) =>
            new((float)(cx + cos * rx * x - sin * ry * y), (float)(cy + sin * rx * x + cos * ry * y));

        private void Close() { _path.CloseFigure(); _current = _start; ResetControls(); }
        private PointF ReadPoint(bool relative)
        {
            float x = _reader.Read(), y = _reader.Read();
            return relative ? new(_current.X + x, _current.Y + y) : new(x, y);
        }
        private void LineTo(PointF point) { _path.AddLine(_current, point); _current = point; ResetControls(); }
        private PointF Reflect(PointF point) => new(2 * _current.X - point.X, 2 * _current.Y - point.Y);
        private static bool Same(PointF a, PointF b) => a.X == b.X && a.Y == b.Y;
        private void ResetControls() { _lastCubic = null; _lastQuadratic = null; }
    }

    private sealed class NumberReader
    {
        private readonly string _text;
        private int _index;
        public NumberReader(string text) => _text = text;
        public bool End { get { Skip(); return _index >= _text.Length; } }
        public bool HasNumber { get { Skip(); return _index < _text.Length && (_text[_index] is '+' or '-' or '.' || char.IsDigit(_text[_index])); } }

        public bool TryReadCommand(out char command)
        {
            Skip();
            if (_index < _text.Length && char.IsLetter(_text[_index])) { command = _text[_index++]; return true; }
            command = '\0'; return false;
        }

        public float Read()
        {
            Skip();
            int start = _index;
            if (_index < _text.Length && _text[_index] is '+' or '-') _index++;
            bool digits = ReadDigits();
            if (_index < _text.Length && _text[_index] == '.') { _index++; digits |= ReadDigits(); }
            if (!digits) throw Error("Expected a number.");
            if (_index < _text.Length && _text[_index] is 'e' or 'E')
            {
                _index++;
                if (_index < _text.Length && _text[_index] is '+' or '-') _index++;
                if (!ReadDigits()) throw Error("Malformed number exponent.");
            }
            if (!float.TryParse(_text.AsSpan(start, _index - start), NumberStyles.Float, CultureInfo.InvariantCulture, out float value) || !float.IsFinite(value))
                throw Error("Invalid or non-finite SVG number.");
            return value;
        }

        public void RequireEnd() { Skip(); if (_index != _text.Length) throw Error("Unexpected content after number list."); }
        public InvalidDataException Error(string message) => new($"{message} Position {_index} in '{_text}'.");
        private bool ReadDigits() { int start = _index; while (_index < _text.Length && char.IsDigit(_text[_index])) _index++; return _index > start; }
        private void Skip() { while (_index < _text.Length && (char.IsWhiteSpace(_text[_index]) || _text[_index] == ',')) _index++; }
    }
}
