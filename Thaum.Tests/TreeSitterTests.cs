using Xunit;
using Microsoft.Extensions.Logging;
using NSubstitute;
using FluentAssertions;
using Thaum.Core.Crawling;
using Thaum.Core.Services;
using Thaum.Core.Models;

namespace Thaum.Tests;

public class TreeSitterTests {
	private readonly ILogger<TreeSitterCrawler.Parser> _mockLogger = Substitute.For<ILogger<TreeSitterCrawler.Parser>>();

	[Fact]
	public void Parse_SimpleEnum_ShouldExtractEnum() {
		// Arrange
		const string SOURCE_CODE = """
		                           namespace Test;

		                           public enum SimpleEnum
		                           {
		                               First,
		                               Second = 5,
		                               Third
		                           }
		                           """;

		using TreeSitterCrawler.Parser parser = new TreeSitterCrawler.Parser("c-sharp");

		// Act
		List<CodeSymbol> symbols = parser.Parse(SOURCE_CODE, "test.cs");

		// Assert
		symbols.Should().Contain(s => s.Kind == SymbolKind.Enum && s.Name == "SimpleEnum");
		symbols.Should().Contain(s => s.Kind == SymbolKind.EnumMember && s.Name == "First");
		symbols.Should().Contain(s => s.Kind == SymbolKind.EnumMember && s.Name == "Second");
		symbols.Should().Contain(s => s.Kind == SymbolKind.EnumMember && s.Name == "Third");
	}

	[Fact]
	public void Parse_ClassWithMethods_ShouldExtractAll() {
		// Arrange
		const string SOURCE_CODE = """
		                           public class TestClass
		                           {
		                               public TestClass() { }
		                               
		                               public void Method() { }
		                               
		                               public string Property { get; set; }
		                           }
		                           """;

		using TreeSitterCrawler.Parser parser = new TreeSitterCrawler.Parser("c-sharp");

		// Act
		List<CodeSymbol> symbols = parser.Parse(SOURCE_CODE, "test.cs");

		// Assert
		symbols.Should().Contain(s => s.Kind == SymbolKind.Class && s.Name == "TestClass");
		symbols.Should().Contain(s => s.Kind == SymbolKind.Constructor && s.Name == "TestClass");
		symbols.Should().Contain(s => s.Kind == SymbolKind.Method && s.Name == "Method");
		symbols.Should().Contain(s => s.Kind == SymbolKind.Property && s.Name == "Property");
	}

	[Fact]
	public void Parse_CompressionLevelEnum_ShouldExtractAllMembers() {
		// Arrange
		const string SOURCE_CODE = """
		                           namespace Thaum.Core.Models;

		                           public enum CompressionLevel {
		                               /// <summary>
		                               /// Standard optimization to essential operational semantics
		                               /// </summary>
		                               Optimize = 1,

		                               /// <summary>
		                               /// Lossless pseudocode golf compression with emergent grammars
		                               /// </summary>
		                               Compress = 2,

		                               /// <summary>
		                               /// Maximal compression with ultra-dense operational embeddings
		                               /// </summary>
		                               Golf = 3,

		                               /// <summary>
		                               /// End-game superposed vector retopologization compression
		                               /// </summary>
		                               Endgame = 4
		                           }
		                           """;

		using TreeSitterCrawler.Parser parser = new TreeSitterCrawler.Parser("c-sharp");

		// Act
		List<CodeSymbol> symbols = parser.Parse(SOURCE_CODE, "CompressionLevel.cs");

		// Assert
		symbols.Should().Contain(s => s.Kind == SymbolKind.Enum && s.Name == "CompressionLevel");
		symbols.Should().Contain(s => s.Kind == SymbolKind.EnumMember && s.Name == "Optimize");
		symbols.Should().Contain(s => s.Kind == SymbolKind.EnumMember && s.Name == "Compress");
		symbols.Should().Contain(s => s.Kind == SymbolKind.EnumMember && s.Name == "Golf");
		symbols.Should().Contain(s => s.Kind == SymbolKind.EnumMember && s.Name == "Endgame");
	}

	[Fact]
	public void Parse_EmptyCode_ShouldReturnEmptyList() {
		// Arrange
		using TreeSitterCrawler.Parser parser = new TreeSitterCrawler.Parser("c-sharp");

		// Act
		List<CodeSymbol> symbols = parser.Parse("", "empty.cs");

		// Assert
		symbols.Should().BeEmpty();
	}

	[Fact]
	public void Parse_Interface_ShouldExtractInterface() {
		// Arrange
		const string SOURCE_CODE = """
		                           namespace Test;

		                           public interface ITestService
		                           {
		                               void DoSomething();
		                               string GetValue();
		                               int Count { get; set; }
		                           }
		                           """;

		using TreeSitterCrawler.Parser parser = new TreeSitterCrawler.Parser("c-sharp");

		// Act
		List<CodeSymbol> symbols = parser.Parse(SOURCE_CODE, "test.cs");

		// Assert
		symbols.Should().Contain(s => s.Kind == SymbolKind.Interface && s.Name == "ITestService");
		symbols.Should().Contain(s => s.Kind == SymbolKind.Method && s.Name == "DoSomething");
		symbols.Should().Contain(s => s.Kind == SymbolKind.Method && s.Name == "GetValue");
		symbols.Should().Contain(s => s.Kind == SymbolKind.Property && s.Name == "Count");
	}

	[Fact]
	public void Parse_Namespace_ShouldExtractNamespace() {
		// Arrange
		const string SOURCE_CODE = """

		                           namespace MyProject.Services
		                           {
		                               public class ServiceClass
		                               {
		                                   public void DoWork() { }
		                               }
		                           }
		                           """;

		using TreeSitterCrawler.Parser parser = new TreeSitterCrawler.Parser("c-sharp");

		// Act
		List<CodeSymbol> symbols = parser.Parse(SOURCE_CODE, "test.cs");

		// Assert
		symbols.Should().Contain(s => s.Kind == SymbolKind.Namespace && s.Name == "MyProject.Services");
		symbols.Should().Contain(s => s.Kind == SymbolKind.Class && s.Name == "ServiceClass");
		symbols.Should().Contain(s => s.Kind == SymbolKind.Method && s.Name == "DoWork");
	}

	[Fact]
	public void Parse_Fields_ShouldExtractFields() {
		// Arrange
		const string SOURCE_CODE = """
		                           public class TestClass
		                           {
		                               private string _name;
		                               public int Age;
		                               private readonly List<string> _items = new();
		                           }
		                           """;

		using TreeSitterCrawler.Parser parser = new TreeSitterCrawler.Parser("c_sharp");

		// Act
		List<CodeSymbol> symbols = parser.Parse(SOURCE_CODE, "test.cs");

		// Assert
		symbols.Should().Contain(s => s.Kind == SymbolKind.Class && s.Name == "TestClass");
		symbols.Should().Contain(s => s.Kind == SymbolKind.Field && s.Name == "_name");
		symbols.Should().Contain(s => s.Kind == SymbolKind.Field && s.Name == "Age");
		symbols.Should().Contain(s => s.Kind == SymbolKind.Field && s.Name == "_items");
	}

	[Fact]
	public void Parse_InvalidSyntax_ShouldNotCrash() {
		// Arrange
		const string                   INVALID_CODE = "public class { invalid syntax }";
		using TreeSitterCrawler.Parser parser       = new TreeSitterCrawler.Parser("c-sharp");

		// Act & Assert - Should not throw
		List<CodeSymbol> symbols = parser.Parse(INVALID_CODE, "invalid.cs");
		symbols.Should().NotBeNull();
	}
}
