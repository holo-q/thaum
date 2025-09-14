using Terminal.Gui;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Thaum.UI.Views;

public class ProgressView : Window {
	private readonly ProgressBar              _progressBar;
	private readonly Label                    _statusLabel;
	private readonly Label                    _detailsLabel;
	private readonly Button                   _cancelButton;
	private          CancellationTokenSource? _cancellationTokenSource;

	public event Action? Cancelled;

	public ProgressView() : base() {
		Title = "Processing";
		Width  = 60;
		Height = 10;

		// Status label
		_statusLabel = new Label {
			X             = 1,
			Y             = 1,
			Width         = Dim.Fill(1),
			Height        = 1,
			Text          = "Processing...",
			TextAlignment = Alignment.Center
		};

		// Progress bar
		_progressBar = new ProgressBar {
			X      = 1,
			Y      = 3,
			Width  = Dim.Fill(1),
			Height = 1
		};

		// Details label (for current operation)
		_detailsLabel = new Label {
			X             = 1,
			Y             = 5,
			Width         = Dim.Fill(1),
			Height        = 2,
			Text          = "",
			TextAlignment = Alignment.Start
		};

		// Cancel button
		_cancelButton = new Button() {
			Text = "Cancel",
			X = Pos.Center(),
			Y = Pos.Bottom(this) - 2
		};
		_cancelButton.Accepting += (sender, e) => OnCancelClicked();

		Add(_statusLabel, _progressBar, _detailsLabel, _cancelButton);
	}

	public void Start(string status, CancellationToken cancellationToken = default) {
		_cancellationTokenSource?.Cancel();
		_cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

		_statusLabel.Text     = status;
		_detailsLabel.Text    = "";
		_progressBar.Fraction = 0f;

		Visible = true;
		SetFocus();
	}

	public void UpdateProgress(float fraction, string? details = null) {
		Application.Invoke(() => {
			_progressBar.Fraction = Math.Clamp(fraction, 0f, 1f);

			if (details != null) {
				_detailsLabel.Text = details;
			}
		});
	}

	public void UpdateStatus(string status) {
		Application.Invoke(() => {
			_statusLabel.Text = status;
		});
	}

	public void Complete(string? finalStatus = null) {
		Application.Invoke(() => {
			if (finalStatus != null) {
				_statusLabel.Text = finalStatus;
			}

			_progressBar.Fraction = 1f;
			_detailsLabel.Text    = "Completed";

			// Auto-hide after a short delay
			Task.Delay(1500).ContinueWith(_ => {
				Application.Invoke(() => Visible = false);
			});
		});
	}

	public void Hide() {
		Visible = false;
		_cancellationTokenSource?.Cancel();
	}

	private void OnCancelClicked() {
		_cancellationTokenSource?.Cancel();
		Cancelled?.Invoke();
		Hide();
	}

	public CancellationToken CancellationToken => _cancellationTokenSource?.Token ?? CancellationToken.None;
}