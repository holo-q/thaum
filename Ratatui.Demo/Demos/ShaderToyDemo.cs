using System.Diagnostics;
using Ratatui.Sugar;
using Thaum.App.RatatuiTUI;
using static System.MathF;

namespace Ratatui.Demo.Demos;

public class ShaderToyDemo : BaseDemo {
	public override string   Name        => "ShaderToy Fractal";
	public override string   Description => "Animated Mandelbrot-style fractal rendered via the compositor";
	public override string[] Tags        => ["shader", "fractal", "gpu", "effects"];

	public override int Run() {
		var shader    = new MandelbrotShader();
		var stopwatch = Stopwatch.StartNew();

		return Rat.Run((frame, events) => {
			foreach (var ev in events) {
				if (ev.Kind != EventKind.Key) continue;
				var code = (KeyCode)ev.Key.Code;
				if (code == KeyCode.ESC || (code == KeyCode.Char && (char)ev.Key.Char is 'q' or 'Q'))
					return false;
			}

			frame.Clear();
			float time = (float)stopwatch.Elapsed.TotalSeconds;
			var   rect = new Rect(0, 0, frame.Width, frame.Height);
			ShaderToy.Render(frame, rect, shader, time);

			using (var hud = new Paragraph("")
				       .AppendLine("ShaderToy Demo", new Style(fg: Colors.LCYAN, bold: true))
				       .AppendLine("Animated Mandelbrot fractal", new Style(fg: Colors.GRAY))
				       .AppendLine("Press Q/Esc to exit", new Style(fg: Colors.YELLOW))) {
				frame.Draw(hud, new Rect(1, 1, Math.Min(32, frame.Width - 2), Math.Min(5, frame.Height - 2)), fgAlpha: 255, bgAlpha: 180);
			}

			frame.Present();
			return true;
		}, fps: 30);
	}

	private sealed class MandelbrotShader : IShaderToy {
		public Rgba Shade(float u, float v, float timeSeconds, int width, int height) {
			float aspect = width / Math.Max(1f, height);
			float zoom   = 0.8f + 0.2f * Sin(timeSeconds * 0.3f);
			float angle  = timeSeconds * 0.1f;

			float x = (u - 0.5f) * 3f * zoom;
			float y = (v - 0.5f) * 3f * zoom;

			float cos = Cos(angle);
			float sin = Sin(angle);
			float rx  = x * cos - y * sin;
			float ry  = x * sin + y * cos;

			float cx = rx * aspect - 0.5f + 0.3f * Cos(timeSeconds * 0.2f);
			float cy = ry - 0.3f * Sin(timeSeconds * 0.15f);

			float zx   = 0f;
			float zy   = 0f;
			int   iter = 0;

			const int MAX_ITER = 120;

			while (zx * zx + zy * zy <= 4f && iter < MAX_ITER) {
				float temp = zx * zx - zy * zy + cx;
				zy = 2f * zx * zy + cy;
				zx = temp;
				iter++;
			}

			if (iter >= MAX_ITER)
				return ShaderToy.FromRgb(0f, 0f, 0f);

			float magnitude = Max(zx * zx + zy * zy, 1e-6f);
			float smooth    = iter - Log(Log(magnitude)) / Log(2f);
			smooth /= MAX_ITER;
			smooth =  Sqrt(Max(0f, smooth));
			float r = 0.5f + 0.5f * Cos(6.28318f * smooth + timeSeconds * 0.4f);
			float g = 0.5f + 0.5f * Cos(6.28318f * (smooth + 0.3f) + timeSeconds * 0.3f);
			float b = 0.5f + 0.5f * Cos(6.28318f * (smooth + 0.6f) + timeSeconds * 0.2f);
			return ShaderToy.FromRgb(r, g, b);
		}
	}
}