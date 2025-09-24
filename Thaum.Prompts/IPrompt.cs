namespace Thaum.Prompts;

public interface IPrompt {
	string Render();
}

public interface IFunctionPrompt : IPrompt {
	string SourceCode { get; set; }
	string SymbolName { get; set; }
	string AvailableKeys { get; set; }
}

public interface IClassPrompt : IPrompt {
	string SourceCode { get; set; }
	string SymbolName { get; set; }
	string AvailableKeys { get; set; }
}

public interface IKeyPrompt : IPrompt {
	string Summaries { get; set; }
	string Level { get; set; }
}