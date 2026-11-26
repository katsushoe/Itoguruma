using System.Drawing.Drawing2D;

namespace Itoguruma.Viewer;

/// <summary>メニュー項目に付ける、MCPサーバー状態のLED画像を描画・保持します。</summary>
internal sealed class StatusLed : IDisposable
{
    private readonly Bitmap _running = Draw(Color.FromArgb(40, 190, 70));
    private readonly Bitmap _notResponding = Draw(Color.FromArgb(240, 170, 30));
    private readonly Bitmap _stopped = Draw(Color.FromArgb(215, 50, 45));
    private readonly Bitmap _unknown = Draw(Color.FromArgb(150, 150, 150));

    /// <summary>状態に対応する画像を返します。null は未確認です。返した画像は破棄しないでください。</summary>
    public Image For(ServerStatus? status) => status?.State switch
    {
        ServerState.Running => _running,
        ServerState.NotResponding => _notResponding,
        ServerState.Stopped => _stopped,
        _ => _unknown
    };

    private static Bitmap Draw(Color color)
    {
        const int size = 16;
        var bitmap = new Bitmap(size, size);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);
        var body = new RectangleF(1.5f, 1.5f, size - 3, size - 3);

        // 中心が明るく縁が暗い球面で、点灯したLEDに見せます。
        using (var path = new GraphicsPath())
        {
            path.AddEllipse(body);
            using var fill = new PathGradientBrush(path)
            {
                CenterColor = ControlPaint.Light(color, 0.6f),
                SurroundColors = [ControlPaint.Dark(color, 0.15f)],
                CenterPoint = new PointF(size * 0.45f, size * 0.4f)
            };
            graphics.FillEllipse(fill, body);
        }
        using (var rim = new Pen(ControlPaint.Dark(color, 0.45f), 1f))
            graphics.DrawEllipse(rim, body);
        using (var highlight = new SolidBrush(Color.FromArgb(170, Color.White)))
            graphics.FillEllipse(highlight, 4.5f, 3.5f, 4f, 3f);
        return bitmap;
    }

    public void Dispose()
    {
        _running.Dispose();
        _notResponding.Dispose();
        _stopped.Dispose();
        _unknown.Dispose();
    }
}
