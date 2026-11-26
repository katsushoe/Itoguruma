using System.Drawing.Drawing2D;

namespace Itoguruma.Viewer;

/// <summary>メニュー項目に付ける、MCPサーバー状態のLED画像を描画・保持します。</summary>
internal sealed class StatusLed : IDisposable
{
    private readonly Bitmap _running = Draw(Color.FromArgb(40, 190, 70));
    private readonly Bitmap _notResponding = Draw(Color.FromArgb(240, 170, 30));
    private readonly Bitmap _error = Draw(Color.FromArgb(215, 50, 45));
    private readonly Bitmap _stopped = Draw(Color.FromArgb(150, 150, 150));

    /// <summary>
    /// 状態に対応する画像を返します。稼働中は緑、応答なしは黄、エラーは赤、停止中と未確認（null）は灰です。
    /// 返した画像は破棄しないでください。
    /// </summary>
    public Image For(ServerStatus? status) => status?.State switch
    {
        ServerState.Running => _running,
        ServerState.NotResponding => _notResponding,
        ServerState.Error => _error,
        _ => _stopped
    };

    private static Bitmap Draw(Color color)
    {
        // メニューの画像領域は16x16に縮尺されるため、正方形のビットマップ内に横長のピル型を描きます。
        const int size = 16;
        var bitmap = new Bitmap(size, size);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);
        var body = new RectangleF(0.5f, 4.5f, size - 1, 7f);

        // 上が明るく下が暗い円筒面で、点灯したLEDに見せます。
        using (var path = Pill(body))
        {
            using var fill = new LinearGradientBrush(body, ControlPaint.Light(color, 0.5f),
                ControlPaint.Dark(color, 0.1f), LinearGradientMode.Vertical);
            graphics.FillPath(fill, path);
            using var rim = new Pen(ControlPaint.Dark(color, 0.45f), 1f);
            graphics.DrawPath(rim, path);
        }
        using (var highlight = Pill(new RectangleF(body.X + 3f, body.Y + 1.2f, body.Width - 6f, 2f)))
        using (var brush = new SolidBrush(Color.FromArgb(160, Color.White)))
            graphics.FillPath(brush, highlight);
        return bitmap;
    }

    /// <summary>両端が半円の角丸長方形（ピル型）の輪郭を作ります。</summary>
    private static GraphicsPath Pill(RectangleF bounds)
    {
        var diameter = Math.Min(bounds.Height, bounds.Width);
        var path = new GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 90, 180);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 180);
        path.CloseFigure();
        return path;
    }

    public void Dispose()
    {
        _running.Dispose();
        _notResponding.Dispose();
        _error.Dispose();
        _stopped.Dispose();
    }
}
