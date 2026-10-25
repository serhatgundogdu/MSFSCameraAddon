using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace CinematicCam.Ui;

/// <summary>Uçağı üstten (burun yukarı) çizen tuval yardımcıları.</summary>
public static class TopView
{
    /// <summary>
    /// Uçak silüetini çizer. toX/toY metre → piksel dönüşümleri; length ve span metre.
    /// inside: yalnızca gövde (kabin yakın planı).
    /// </summary>
    public static void DrawAircraft(Canvas canvas, Func<double, double> toX, Func<double, double> toY, double noseZ, double length, double span, double fuselageHalfWidth, bool inside)
    {
        double tailZ = noseZ - length, fw = fuselageHalfWidth;
        var fill = U.Hex("#262C37");
        var stroke = U.Hex("#4A5261");

        var body = new PathFigure { StartPoint = new Point(toX(0), toY(noseZ + 1.5)), IsClosed = true };
        body.Segments.Add(new BezierSegment(new Point(toX(fw), toY(noseZ + 1)), new Point(toX(fw), toY(noseZ - 2)), new Point(toX(fw), toY(noseZ - 4)), true));
        body.Segments.Add(new LineSegment(new Point(toX(fw), toY(tailZ + 3)), true));
        body.Segments.Add(new LineSegment(new Point(toX(0.5), toY(tailZ)), true));
        body.Segments.Add(new LineSegment(new Point(toX(-0.5), toY(tailZ)), true));
        body.Segments.Add(new LineSegment(new Point(toX(-fw), toY(tailZ + 3)), true));
        body.Segments.Add(new LineSegment(new Point(toX(-fw), toY(noseZ - 4)), true));
        body.Segments.Add(new BezierSegment(new Point(toX(-fw), toY(noseZ - 2)), new Point(toX(-fw), toY(noseZ + 1)), new Point(toX(0), toY(noseZ + 1.5)), true));

        var geometry = new PathGeometry();
        if (!inside)
        {
            double wz = noseZ - length * 0.4;
            foreach (int side in new[] { 1, -1 })
            {
                geometry.Figures.Add(Poly(new Point(toX(side * fw), toY(wz)), new Point(toX(side * span / 2), toY(wz - length * 0.21)),
                    new Point(toX(side * span / 2), toY(wz - length * 0.27)), new Point(toX(side * fw), toY(wz - length * 0.16))));
                double ts = Math.Max(span * 0.17, 3);
                geometry.Figures.Add(Poly(new Point(toX(side * (fw - 0.4)), toY(tailZ + length * 0.13)), new Point(toX(side * ts), toY(tailZ + length * 0.03)),
                    new Point(toX(side * ts), toY(tailZ)), new Point(toX(side * 0.5), toY(tailZ + length * 0.04))));
            }
        }
        geometry.Figures.Add(body);
        canvas.Children.Add(new Path { Data = geometry, Fill = fill, Stroke = stroke, StrokeThickness = 1.2, StrokeLineJoin = PenLineJoin.Round });
    }

    private static PathFigure Poly(params Point[] points)
    {
        var f = new PathFigure { StartPoint = points[0], IsClosed = true };
        foreach (var p in points.Skip(1)) f.Segments.Add(new LineSegment(p, true));
        return f;
    }

    public static void Grid(Canvas canvas, double w, double h, double step)
    {
        var brush = U.Hex("#1C2028");
        for (double x = step; x < w; x += step) canvas.Children.Add(new Line { X1 = x, X2 = x, Y1 = 0, Y2 = h, Stroke = brush });
        for (double y = step; y < h; y += step) canvas.Children.Add(new Line { X1 = 0, X2 = w, Y1 = y, Y2 = y, Stroke = brush });
    }

    /// <summary>Kamera noktası ve hedefe bakan görüş konisi.</summary>
    public static void Camera(Canvas canvas, Point cam, Point target, double reach = 70, double spreadRad = 0.42)
    {
        var d = target - cam;
        d.Normalize();
        Point Rot(double a) => new(cam.X + (d.X * Math.Cos(a) - d.Y * Math.Sin(a)) * reach, cam.Y + (d.Y * Math.Cos(a) + d.X * Math.Sin(a)) * reach);
        var cone = new Polygon
        {
            Points = new PointCollection { cam, Rot(spreadRad), Rot(-spreadRad) },
            Fill = new SolidColorBrush(Color.FromArgb(36, 0xFF, 0xB5, 0x47)),
            Stroke = new SolidColorBrush(Color.FromArgb(150, 0xFF, 0xB5, 0x47)),
            StrokeThickness = 1,
        };
        canvas.Children.Add(cone);
        Dot(canvas, cam, 18, U.B("Accent"));
        Dot(canvas, cam, 7, U.B("AccentInk"));
    }

    public static Ellipse Dot(Canvas canvas, Point at, double size, Brush fill, Brush? stroke = null)
    {
        var e = new Ellipse { Width = size, Height = size, Fill = fill, Stroke = stroke, StrokeThickness = stroke == null ? 0 : 2 };
        Canvas.SetLeft(e, at.X - size / 2);
        Canvas.SetTop(e, at.Y - size / 2);
        canvas.Children.Add(e);
        return e;
    }
}
