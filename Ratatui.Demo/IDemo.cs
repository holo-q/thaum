namespace Ratatui.Demo;

public interface IDemo {
	string   Name        { get; }
	string   Description { get; }
	string[] Tags        { get; }
	int      Run();
}

public interface IEmbeddedDemo {
    Thaum.App.RatatuiTUI.Screen Create(Program.DemoTUI app);
}
