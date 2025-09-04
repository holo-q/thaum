using Terminal.Gui;

namespace Thaum.UI.Views;

public class SimpleProjectView : FrameView {
	private readonly ListView     _listView;
	private readonly List<string> _files = new();

	public event Action<string?>? FileSelected;

	public SimpleProjectView() : base("Project Files") {
		_listView = new ListView {
			X      = 0,
			Y      = 0,
			Width  = Dim.Fill(),
			Height = Dim.Fill()
		};

		_listView.OpenSelectedItem += (e) => {
			if (e.Item < _files.Count) {
				FileSelected?.Invoke(_files[e.Item]);
			}
		};

		Add(_listView);
	}

	public void LoadFiles(string projectPath) {
		_files.Clear();

		try {
			List<string> sourceFiles = Directory.GetFiles(projectPath, "*.*", SearchOption.AllDirectories)
				.Where(f => IsSourceFile(f))
				.Select(f => Path.GetRelativePath(projectPath, f))
				.OrderBy(f => f)
				.ToList();

			_files.AddRange(sourceFiles);
			_listView.SetSource(_files);

			if (_files.Any()) {
				_listView.SelectedItem = 0;
			}
		} catch (Exception) {
			// Ignore errors loading files
		}
	}

	private static bool IsSourceFile(string filePath) {
		string extension = Path.GetExtension(filePath).ToLowerInvariant();
		return extension is ".py" or ".cs" or ".js" or ".ts" or ".rs" or ".go" or ".java" or ".cpp" or ".cc" or ".cxx" or ".c" or ".h" or ".hpp";
	}
}