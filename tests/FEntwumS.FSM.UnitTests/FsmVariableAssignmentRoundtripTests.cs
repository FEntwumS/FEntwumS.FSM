using System.Linq;
using System.Xml.Linq;
using FEntwumS.FSM.ViewModels;
using Xunit;

namespace FEntwumS.FSM.UnitTests;

public class FsmVariableAssignmentRoundtripTests
{
    private static readonly XNamespace ScxmlNs = "http://www.w3.org/2005/07/scxml";

    [Fact]
    public void SyncVariablesMetadata_MapsFrontendTypesToBackendXml()
    {
        var document = XDocument.Parse(@"<scxml xmlns='http://www.w3.org/2005/07/scxml'><states /></scxml>");

        var variables = new[]
        {
            new VariableDefinitionViewModel { Name = "signedValue", Type = "SIGNED", Size = "16" },
            new VariableDefinitionViewModel { Name = "unsignedValue", Type = "UNSIGNED", Size = "8" },
            new VariableDefinitionViewModel { Name = "packedBits", Type = "BIT_N", Size = "12" },
            new VariableDefinitionViewModel { Name = "nibbleValue", Type = "BIT_N", Size = "4" },
            new VariableDefinitionViewModel { Name = "byteValue", Type = "BIT_N", Size = "8" },
            new VariableDefinitionViewModel { Name = "bitValue", Type = "BIT", Size = "1" }
        };

        FsmXmlStateHelper.SyncVariablesMetadata(document, ScxmlNs, variables);

        var varElements = document.Root?.Element(ScxmlNs + "variables")?.Elements(ScxmlNs + "var").ToList();

        Assert.NotNull(varElements);
        Assert.Equal(6, varElements!.Count);

        Assert.Equal("signedValue", varElements[0].Attribute("name")?.Value);
        Assert.Equal("integer", varElements[0].Attribute("type")?.Value);
        Assert.Equal("16", varElements[0].Attribute("size")?.Value);

        Assert.Equal("unsignedValue", varElements[1].Attribute("name")?.Value);
        Assert.Equal("unsigned", varElements[1].Attribute("type")?.Value);
        Assert.Equal("8", varElements[1].Attribute("size")?.Value);

        Assert.Equal("packedBits", varElements[2].Attribute("name")?.Value);
        Assert.Equal("vector", varElements[2].Attribute("type")?.Value);
        Assert.Equal("12", varElements[2].Attribute("size")?.Value);

        Assert.Equal("nibbleValue", varElements[3].Attribute("name")?.Value);
        Assert.Equal("nibble", varElements[3].Attribute("type")?.Value);
        Assert.Null(varElements[3].Attribute("size"));

        Assert.Equal("byteValue", varElements[4].Attribute("name")?.Value);
        Assert.Equal("byte", varElements[4].Attribute("type")?.Value);
        Assert.Null(varElements[4].Attribute("size"));

        Assert.Equal("bitValue", varElements[5].Attribute("name")?.Value);
        Assert.Equal("bit", varElements[5].Attribute("type")?.Value);
        Assert.Null(varElements[5].Attribute("size"));
    }

    [Fact]
    public void ReadVariables_MapsBackendXmlBackToFrontendTypes()
    {
        var document = XDocument.Parse(@"<scxml xmlns='http://www.w3.org/2005/07/scxml'>
  <variables>
    <var name='signedValue' type='integer' size='16' />
    <var name='unsignedValue' type='unsigned' size='8' />
    <var name='packedBits' type='vector' size='12' />
    <var name='nibbleValue' type='nibble' />
    <var name='byteValue' type='byte' />
    <var name='bitValue' type='bit' />
  </variables>
</scxml>");

        var variables = FsmXmlStateHelper.ReadVariables(document, ScxmlNs);

        Assert.Collection(
            variables,
            item =>
            {
                Assert.Equal("signedValue", item.Name);
                Assert.Equal("SIGNED", item.Type);
                Assert.Equal("16", item.Size);
            },
            item =>
            {
                Assert.Equal("unsignedValue", item.Name);
                Assert.Equal("UNSIGNED", item.Type);
                Assert.Equal("8", item.Size);
            },
            item =>
            {
                Assert.Equal("packedBits", item.Name);
                Assert.Equal("BIT_N", item.Type);
                Assert.Equal("12", item.Size);
            },
            item =>
            {
                Assert.Equal("nibbleValue", item.Name);
                Assert.Equal("BIT_N", item.Type);
                Assert.Equal("4", item.Size);
            },
            item =>
            {
                Assert.Equal("byteValue", item.Name);
                Assert.Equal("BIT_N", item.Type);
                Assert.Equal("8", item.Size);
            },
            item =>
            {
                Assert.Equal("bitValue", item.Name);
                Assert.Equal("BIT", item.Type);
                Assert.Empty(item.Size);
            });
    }

    [Fact]
    public void CreateOnEntryElement_RoundTripsVariableAssignmentSyntaxAndDecimalNumbers()
    {
        var state = new StateItemViewModel
        {
            VariableAssignments = "counter++; limit = #10; mode = signal_name"
        };

        var onEntryElement = FsmXmlStateHelper.CreateOnEntryElement(state, ScxmlNs);

        Assert.NotNull(onEntryElement);

        var assignElements = onEntryElement!.Elements(ScxmlNs + "assign").ToList();
        Assert.Equal(3, assignElements.Count);
        Assert.Equal("counter", assignElements[0].Attribute("variable")?.Value);
        Assert.Equal("counter + 1", assignElements[0].Attribute("expr")?.Value);
        Assert.Equal("limit", assignElements[1].Attribute("variable")?.Value);
        Assert.Equal("#10", assignElements[1].Attribute("expr")?.Value);
        Assert.Equal("mode", assignElements[2].Attribute("variable")?.Value);
        Assert.Equal("signal_name", assignElements[2].Attribute("expr")?.Value);

        var stateElement = new XElement(ScxmlNs + "state", new XAttribute("id", "S1"), onEntryElement);
        var roundTripped = FsmXmlStateHelper.ReadVariableAssignments(stateElement, ScxmlNs);

        Assert.Equal("counter++; limit = #10; mode = signal_name", roundTripped);
    }

    [Theory]
    [InlineData("binaryValue = 1011", "binaryValue", "1011", "binaryValue = 1011")]
    [InlineData("hexValue = 0xA2", "hexValue", "0xA2", "hexValue = 0xA2")]
    [InlineData("decimalValue = #-37", "decimalValue", "#-37", "decimalValue = #-37")]
    [InlineData("singleBitValue = 0", "singleBitValue", "0", "singleBitValue = 0")]
    [InlineData("leadingZeroValue = 01", "leadingZeroValue", "01", "leadingZeroValue = 01")]
    public void CreateOnEntryElement_AndReadVariableAssignments_HandleNumberNotation(
        string displayAssignment,
        string expectedVariable,
        string expectedBackendExpr,
        string expectedRoundTrippedAssignment)
    {
        var state = new StateItemViewModel
        {
            VariableAssignments = displayAssignment
        };

        var onEntryElement = FsmXmlStateHelper.CreateOnEntryElement(state, ScxmlNs);

        Assert.NotNull(onEntryElement);

        var assignElement = Assert.Single(onEntryElement!.Elements(ScxmlNs + "assign"));
        Assert.Equal(expectedVariable, assignElement.Attribute("variable")?.Value);
        Assert.Equal(expectedBackendExpr, assignElement.Attribute("expr")?.Value);

        var roundTrippedStateElement = new XElement(ScxmlNs + "state", new XAttribute("id", "S1"), onEntryElement);
        var roundTrippedAssignment = FsmXmlStateHelper.ReadVariableAssignments(roundTrippedStateElement, ScxmlNs);

        Assert.Equal(expectedRoundTrippedAssignment, roundTrippedAssignment);
    }
}