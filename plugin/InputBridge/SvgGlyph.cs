using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml;

namespace BilibiliPotPlayer
{
    // Read the packaged monochrome SVG's original paths. Supports the packaged icons' SVG path commands;
    // unsupported SVG syntax fails visibly instead of substituting an approximate drawing.
    internal sealed class SvgGlyph
    {
        readonly GraphicsPath outline = new GraphicsPath(FillMode.Winding);
        readonly RectangleF bounds;
        internal float Aspect { get { return bounds.Width / bounds.Height; } }
        internal SvgGlyph(string resource)
        {
            using (Stream data = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
            {
                if (data == null) throw new InvalidDataException("Missing SVG resource: " + resource);
                XmlDocument document = new XmlDocument(); document.XmlResolver = null;
                using (XmlReader reader = XmlReader.Create(data, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
                    document.Load(reader);
                foreach (XmlElement element in document.SelectNodes("//*[local-name()='path']")) Parse(element.GetAttribute("d"));
            }
            bounds = outline.GetBounds();
            if (bounds.Width <= 0 || bounds.Height <= 0) throw new InvalidDataException("Empty SVG glyph.");
        }
        void Parse(string data)
        {
            MatchCollection tokens = Regex.Matches(data, @"[A-Za-z]|[-+]?(?:\d*\.)?\d+(?:[eE][-+]?\d+)?");
            int index = 0; char command = '\0'; PointF current = PointF.Empty, start = PointF.Empty, control = PointF.Empty; char previous = '\0';
            Func<float> number = delegate
            {
                if (index >= tokens.Count || Char.IsLetter(tokens[index].Value[0])) throw new InvalidDataException("Invalid SVG path.");
                return Single.Parse(tokens[index++].Value, CultureInfo.InvariantCulture);
            };
            Func<bool, PointF> point = delegate(bool relative)
            { float x = number(), y = number(); return new PointF(x + (relative ? current.X : 0), y + (relative ? current.Y : 0)); };
            while (index < tokens.Count)
            {
                if (Char.IsLetter(tokens[index].Value[0])) command = tokens[index++].Value[0];
                bool relative = Char.IsLower(command); PointF next;
                switch (Char.ToUpperInvariant(command))
                {
                    case 'M':
                        current = point(relative); start = current; outline.StartFigure(); command = relative ? 'l' : 'L'; break;
                    case 'L':
                        next = point(relative); outline.AddLine(current, next); current = next; break;
                    case 'H':
                        next = new PointF(number() + (relative ? current.X : 0), current.Y);
                        outline.AddLine(current, next); current = next; break;
                    case 'V':
                        next = new PointF(current.X, number() + (relative ? current.Y : 0));
                        outline.AddLine(current, next); current = next; break;
                    case 'C':
                        PointF first = point(relative), second = point(relative); next = point(relative);
                        outline.AddBezier(current, first, second, next); current = next; control = second; break;
                    case 'S':
                        PointF reflected = Char.ToUpperInvariant(previous) == 'C' || Char.ToUpperInvariant(previous) == 'S' ? new PointF(2 * current.X - control.X, 2 * current.Y - control.Y) : current;
                        PointF smooth = point(relative); next = point(relative);
                        outline.AddBezier(current, reflected, smooth, next); current = next; control = smooth; break;
                    case 'A':
                        float rx = number(), ry = number(), rotation = number(); bool large = number() != 0, sweep = number() != 0;
                        next = point(relative); Arc(current, next, rx, ry, rotation, large, sweep); current = next; break;
                    case 'Z':
                        outline.CloseFigure(); current = start; command = '\0'; break;
                    default: throw new InvalidDataException("Unsupported SVG path command: " + command);
                }
                previous = command;
            }
        }
        void Arc(PointF from, PointF to, double rx, double ry, double degrees, bool large, bool sweep)
        {
            rx = Math.Abs(rx); ry = Math.Abs(ry);
            if (from == to) return;
            if (rx == 0 || ry == 0) { outline.AddLine(from, to); return; }
            double phi = degrees * Math.PI / 180, cos = Math.Cos(phi), sin = Math.Sin(phi);
            double dx = (from.X - to.X) / 2, dy = (from.Y - to.Y) / 2, x = cos * dx + sin * dy, y = -sin * dx + cos * dy;
            double stretch = x * x / (rx * rx) + y * y / (ry * ry);
            if (stretch > 1) { rx *= Math.Sqrt(stretch); ry *= Math.Sqrt(stretch); }
            double factor = (large == sweep ? -1 : 1) * Math.Sqrt(Math.Max(0, (rx * rx * ry * ry - rx * rx * y * y - ry * ry * x * x) / (rx * rx * y * y + ry * ry * x * x)));
            double cx = factor * rx * y / ry, cy = -factor * ry * x / rx;
            double centerX = cos * cx - sin * cy + (from.X + to.X) / 2, centerY = sin * cx + cos * cy + (from.Y + to.Y) / 2;
            double angle = Math.Atan2((y - cy) / ry, (x - cx) / rx), end = Math.Atan2((-y - cy) / ry, (-x - cx) / rx), delta = end - angle;
            if (sweep && delta < 0) delta += Math.PI * 2; if (!sweep && delta > 0) delta -= Math.PI * 2;
            int parts = Math.Max(1, (int)Math.Ceiling(Math.Abs(delta) / (Math.PI / 2))); double step = delta / parts;
            Func<double, double, PointF> mapped = delegate(double ux, double uy) { return new PointF((float)(centerX + cos * rx * ux - sin * ry * uy), (float)(centerY + sin * rx * ux + cos * ry * uy)); };
            PointF current = from;
            for (int i = 0; i < parts; i++)
            {
                double a = angle + step * i, b = a + step, k = 4.0 / 3 * Math.Tan(step / 4);
                PointF first = mapped(Math.Cos(a) - k * Math.Sin(a), Math.Sin(a) + k * Math.Cos(a)), second = mapped(Math.Cos(b) + k * Math.Sin(b), Math.Sin(b) - k * Math.Cos(b)), next = i == parts - 1 ? to : mapped(Math.Cos(b), Math.Sin(b));
                outline.AddBezier(current, first, second, next); current = next;
            }
        }
        internal void Draw(Graphics graphics, float x, float y, float height, Color color)
        {
            using (GraphicsPath rendered = (GraphicsPath)outline.Clone())
            using (Matrix transform = new Matrix())
            using (Brush fill = new SolidBrush(color))
            {
                transform.Translate(-bounds.Left, -bounds.Top);
                transform.Scale(height / bounds.Height, height / bounds.Height, MatrixOrder.Append);
                transform.Translate(x, y, MatrixOrder.Append);
                rendered.Transform(transform); graphics.FillPath(fill, rendered);
            }
        }
    }
}
