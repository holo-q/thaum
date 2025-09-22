using System;
using System.Diagnostics;
using Ratatui;
using Ratatui.Sugar;
using Thaum.App.RatatuiTUI;

namespace Ratatui.Demo.Demos;

public class ShaderToyDemo : BaseDemo {
	public override string Name => "ShaderToy Fractal";
	public override string Description => "Animated Mandelbrot-style fractal rendered via the compositor";
	public override string[] Tags => ["shader", "fractal", "gpu", "effects"];

	public override int Run() {
		var shader = new MandelbrotShader();
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
			var rect = new Rect(0, 0, frame.Width, frame.Height);
			ShaderToy.Render(frame, rect, shader, time);

			using (var hud = new Paragraph("")
				         .AppendLine("ShaderToy Demo", new Style(fg: Colors.LightCyan, bold: true))
				         .AppendLine("Animated Mandelbrot fractal", new Style(fg: Colors.Gray))
				         .AppendLine("Press Q/Esc to exit", new Style(fg: Colors.Yellow))) {
				frame.Draw(hud, new Rect(1, 1, Math.Min(32, frame.Width - 2), Math.Min(5, frame.Height - 2)), BlendMode.Over, fgAlpha: 255, bgAlpha: 180);
			}

			frame.Present();
			return true;
		}, fps: 30);
	}

	private sealed class MandelbrotShader : IShaderToy {
		public Rgba Shade(float u, float v, float timeSeconds, int width, int height) {
			float aspect = width / Math.Max(1f, height);
			float zoom = 0.8f + 0.2f * MathF.Sin(timeSeconds * 0.3f);
			float angle = timeSeconds * 0.1f;

			float x = (u - 0.5f) * 3f * zoom;
			float y = (v - 0.5f) * 3f * zoom;

			float cosA = MathF.Cos(angle);
			float sinA = MathF.Sin(angle);
			float rotX = x * cosA - y * sinA;
			float rotY = x * sinA + y * cosA;

			float cx = rotX * aspect - 0.5f + 0.3f * MathF.Cos(timeSeconds * 0.2f);
			float cy = rotY - 0.3f * MathF.Sin(timeSeconds * 0.15f);

			float zx = 0f;
			float zy = 0f;
			int iter = 0;
			const int maxIter = 120;

			while (zx * zx + zy * zy <= 4f && iter < maxIter) {
				float temp = zx * zx - zy * zy + cx;
				zy = 2f * zx * zy + cy;
				zx = temp;
				iter++;
			}

			if (iter >= maxIter)
				return ShaderToy.FromRgb(0f, 0f, 0f);

			float magnitude = MathF.Max(zx * zx + zy * zy, 1e-6f);
			float smooth = iter - MathF.Log(MathF.Log(magnitude)) / MathF.Log(2f);
			smooth /= maxIter;
			smooth = MathF.Sqrt(MathF.Max(0f, smooth));
			float r = 0.5f + 0.5f * MathF.Cos(6.28318f * smooth + timeSeconds * 0.4f);
			float g = 0.5f + 0.5f * MathF.Cos(6.28318f * (smooth + 0.3f) + timeSeconds * 0.3f);
			float b = 0.5f + 0.5f * MathF.Cos(6.28318f * (smooth + 0.6f) + timeSeconds * 0.2f);
			return ShaderToy.FromRgb(r, g, b);
		}
	}
}
