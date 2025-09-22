using System;
using System.Diagnostics;
using Ratatui;
using Ratatui.Sugar;
using Thaum.App.RatatuiTUI;

namespace Ratatui.Demo.Demos;

public sealed class DemosceneShowcaseDemo : BaseDemo {
	public override string   Name        => "Demoscene Showcase";
	public override string   Description => "Shader backdrop, starfield, and scroller – a mini demoscene in your terminal";
	public override string[] Tags        => ["shader", "starfield", "plasma", "scroller", "demo"];

	private readonly Star[]       _stars;
	private readonly PlasmaShader _plasma     = new();
	private readonly Stopwatch    _clock      = Stopwatch.StartNew();
	private readonly string       _scrollText = "   RATATUI.CS DEMOSCENE // TERMINAL DREAMSCAPE // PRESS Q TO EXIT   ";

	public DemosceneShowcaseDemo() {
		var rng = new Random(1234);
		_stars = new Star[180];
		for (int i = 0; i < _stars.Length; i++) {
			_stars[i] = new Star {
				X     = (float)rng.NextDouble(),
				Y     = (float)rng.NextDouble(),
				Speed = 0.05f + 0.35f * (float)rng.NextDouble(),
				Hue   = (float)rng.NextDouble()
			};
		}
	}

	public override int Run() {
		_clock.Restart();
		float scrollOffset = 0;

		return Rat.Run((frame, events) => {
			float dt = 1f / 60f;
			foreach (var ev in events) {
				if (ev.Kind != EventKind.Key) continue;
				var code = (KeyCode)ev.Key.Code;
				if (code == KeyCode.ESC || (code == KeyCode.Char && (char)ev.Key.Char is 'q' or 'Q'))
					return false;
			}

			frame.Clear();
			float time = (float)_clock.Elapsed.TotalSeconds;

			ShaderToy.Render(frame, new Rect(0, 0, frame.Width, frame.Height), _plasma, time);

			UpdateStars(frame, dt);

			scrollOffset += dt * 12f;
			DrawScroller(frame, time, scrollOffset);

			DrawLogo(frame, time);

			frame.Present();
			return true;
		}, fps: 60);
	}

	private void UpdateStars(Rat.Frame frame, float dt) {
		int w      = frame.Width;
		int h      = frame.Height;
		var target = frame.Target;
		for (int i = 0; i < _stars.Length; i++) {
			var star = _stars[i];
			star.X += star.Speed * dt;
			if (star.X > 1f) star.X -= 1f;
			int px                  = (int)(star.X * (w - 1));
			int py                  = (int)(star.Y * (h - 1));
			if (px < 0 || px >= w || py < 0 || py >= h) {
				_stars[i] = star;
				continue;
			}

			star.Hue = (star.Hue + dt * 0.2f) % 1f;
			var     fg   = HsvToRgba(star.Hue, 0.8f, 1f);
			ref var cell = ref target.Ref(px, py);
			var     bg   = cell.Bg;
			cell.Ch    = star.Speed > 0.25f ? '✶' : '·';
			cell.Fg    = fg;
			cell.Bg    = bg;
			cell.Mods  = 0;
			cell.Flags = 0;
			_stars[i]  = star;
		}
	}

	private void DrawScroller(Rat.Frame frame, float time, float offset) {
		int w = frame.Width;
		if (w <= 0) return;
		int y = Math.Max(1, frame.Height - 4);

		float speed     = 8f;
		float scroll    = offset * speed;
		int   baseIndex = (int)MathF.Floor(scroll);


		for (int i = 0; i < w + 20; i++) {
			int index            = (baseIndex + i) % _scrollText.Length;
			if (index < 0) index += _scrollText.Length;
			char ch              = _scrollText[index];
			int  px              = (w - 1) - i;
			int  py              = y + (int)(MathF.Sin((i * 0.15f) + time * 2f) * 1.5f);
			if (px < 0 || px >= frame.Width || py < 0 || py >= frame.Height) continue;

			float   hue   = ((i * 0.02f) + time * 0.3f) % 1f;
			var     color = HsvToRgba(hue, 0.8f, 1f);
			ref var cell  = ref frame.Target.Ref(px, py);
			var     bg    = cell.Bg;
			cell.Ch    = ch;
			cell.Fg    = color;
			cell.Bg    = bg;
			cell.Mods  = 0;
			cell.Flags = 0;
		}
	}

	private void DrawLogo(Rat.Frame frame, float time) {
		int w = frame.Width;
		int x = Math.Max(2, w / 2 - 18);
		int y = Math.Max(2, frame.Height / 2 - 6);

		double pulse  = (Math.Sin(time * 3) + 1) * 0.5;
		var    glow   = new Style(fg: Colors.White, bg: Colors.Blue, bold: true);
		var    accent = new Style(fg: Colors.LightCyan, bold: true);

		using var banner = new Paragraph("")
			.AppendLine("    ▄█████████▄", accent)
			.AppendLine("   ███▀▀▀▀▀▀███", accent)
			.AppendLine("   ███  RAT  ███", glow)
			.AppendLine("   ███  DEMO ███", glow)
			.AppendLine("    ▀█████████▀", accent)
			.AppendLine("", accent)
			.AppendLine($"   time {time:0.00}s  fps 60  pulse {pulse:0.00}", new Style(fg: Colors.Gray));
		frame.Draw(banner, new Rect(x, y, Math.Min(36, frame.Width - x - 2), 7), BlendMode.Over, fgAlpha: 255, bgAlpha: 200);
	}

	private static Rgba HsvToRgba(float h, float s, float v) {
		h = h - MathF.Floor(h);
		float c = v * s;
		float x = c * (1 - MathF.Abs((h * 6 % 2) - 1));
		float m = v - c;

		float r, g, b;
		if (h < 1f / 6f) (r, g, b)      = (c, x, 0);
		else if (h < 2f / 6f) (r, g, b) = (x, c, 0);
		else if (h < 3f / 6f) (r, g, b) = (0, c, x);
		else if (h < 4f / 6f) (r, g, b) = (0, x, c);
		else if (h < 5f / 6f) (r, g, b) = (x, 0, c);
		else (r, g, b)                  = (c, 0, x);

		return ShaderToy.FromRgb(r + m, g + m, b + m);
	}

	private struct Star {
		public float X;
		public float Y;
		public float Speed;
		public float Hue;
	}

	private sealed class PlasmaShader : IShaderToy {
		public Rgba Shade(float u, float v, float timeSeconds, int width, int height) {
			float uvx   = u * 2f - 1f;
			float uvy   = v * 2f - 1f;
			float r     = MathF.Sqrt(uvx * uvx + uvy * uvy);
			float angle = MathF.Atan2(uvy, uvx);
			float waves = MathF.Sin(3f * r - timeSeconds * 2f) + MathF.Sin(angle * 4f + timeSeconds);
			float color = 0.5f + 0.5f * MathF.Sin(waves + timeSeconds);
			float cr    = 0.6f + 0.4f * MathF.Sin(timeSeconds + waves * 1.3f);
			float cg    = 0.6f + 0.4f * MathF.Sin(timeSeconds * 0.7f + waves * 1.7f + 1f);
			float cb    = 0.6f + 0.4f * MathF.Sin(timeSeconds * 0.9f + waves * 1.9f + 2f);
			return ShaderToy.FromRgb(color * cr, color * cg, color * cb);
		}
	}
}