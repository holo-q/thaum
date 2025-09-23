namespace Ratatui.Demo;

public interface IDemo {
	string   Name        { get; }
	string   Description { get; }
	string[] Tags        { get; }
	int      Run();
}