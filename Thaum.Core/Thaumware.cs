namespace Thaum.Core;

public interface IArtifact {
	public string GetHolowareContent();
}

/// <summary>
/// A chat conversation between user and assistant.
/// </summary>
public class Conversation : IArtifact {
	public string GetHolowareContent() => throw new NotImplementedException();

	// TODO utility functions to load in all the OpenAI conversation format stuff
}

// CodeMap : IFragment

public class Hol {
	public struct State {
		public IArtifact? artifact;
		public string     content;
	}

	public readonly List<State> history = new List<State>();

	public State Current => history[-1];

	public struct Get {
		public readonly Hol hol;

		public string output;

		public Get(Hol hol) {
			this.hol = hol;
		}

		public void mut(string rewritePrompt = null) {
			// TODO rewrites the hol
			throw new NotImplementedException();
		}
	}

	public static void SetModel(string kimiK2) {
		throw new NotImplementedException(); // TODO set the default model with which to run
	}

	/// <summary>
	/// Choose the LLM to run prompts in this holoware context
	/// </summary>
	/// <param name="name"></param>
	/// <exception cref="NotImplementedException"></exception>
	public static void llm(string name) {
		throw new NotImplementedException(); // TODO set the model with which to run (overrides default global set with SetModel)
	}


	/// <summary>
	/// Call a prompt with the following shape:
	///
	/// inputs: {input}, ...
	/// outputs: {output}
	///
	/// The state is mutated with {output}
	/// </summary>
	public Hol mut(string compact) {
		throw new NotImplementedException(); // TODO call a prompt (must validate that it has an {input})
	}

	/// <summary>
	/// Call a prompt with the following shape:
	///
	/// inputs: {input}, ...
	/// outputs: {output}
	///
	/// The result is a captured {output} that can be
	/// used as an artifact to mutate the state.
	/// </summary>
	public Get get(string empty) {
		throw new NotImplementedException(); // TODO call a prompt (must validate that it has an {input})
	}

	public string render() {
		throw new NotImplementedException();
	}

	public static implicit operator string(Hol hol) {
		return hol.render();
	}
}

public static class HolExt {
	public static Hol hol(this IArtifact frag) {
		throw new NotImplementedException(); // TODO
	}
}

/// <summary>
/// Call this:
///
/// Hol.SetModel("moonshotai/kimi-k2");
/// </summary>
public static class Thaumware {
	// /// <summary>
	// /// This is how /compact is implement in CLI agents
	// /// </summary>
	// public static string Compact(IArtifact input) {
	// 	return input.hol().mut("compact");
	// }
	//
	// /// <summary>
	// /// This is what we recommend
	// /// </summary>
	// public static string CompactConversation(Conversation chat) {
	// 	Hol h      = chat.hol();
	// 	var tuples = ???; // TODO cool selector business to take tuples of (0,1,2), (1,2,3), ...
	// 	foreach (HolRef t in tuples) {
	// 		t.ctx()              // HolRef->HolRef is {context} variable
	// 			[1]              // HolRef->HolRef->Hol is targeted {input}
	// 			.mut("compact"); // mutates {input} inside of entire Hol context
	// 	}
	//
	// 	// TODO So technically it can be written in one line as:
	// 	chat.hol().tuples(3).ctx()[1].mut("compact");
	//
	// 	// If we add the return value of tuples is some special HolList with its own special dsl api
	// }
	//
	// public static string Defragment(IArtifact input) {
	// 	Hol h = input.hol(); // extension methods for all sorts of types; load the fragment into an holoware context, becomes h / {input}
	// 	h.mut("compact");    // invoke compact.hol with h content as {input}; result h is a clone, stores link to previous h-1 starting state of {input}
	// 	h.get("qa").mut();   // queries the current h content with a prompt that has an {input} (must validate)
	//
	// 	return h;
	// }
}