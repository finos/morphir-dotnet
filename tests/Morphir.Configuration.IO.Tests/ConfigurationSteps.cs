using FluentAssertions;
using Microsoft.FSharp.Collections;
using Morphir.Configuration;
using Morphir.Configuration.IO;

namespace Morphir.Configuration.IO.Tests;

[Binding]
public sealed class ConfigurationSteps
{
    private readonly List<SourceInput> _documents = [];

    [Given("equivalent project documents in JSON5 YAML and TOML")]
    public void EquivalentDocuments()
    {
        _documents.Add(ConfigurationReader.ReadText("morphir.json5", "{project:{name:'Acme.Orders',source_directory:'src',exposed_modules:['Orders.Api',]},custom:{count:4,strict:true,text:'4'}}"));
        _documents.Add(ConfigurationReader.ReadText("morphir.yaml", "project:\n  name: Acme.Orders\n  source_directory: src\n  exposed_modules: [Orders.Api]\ncustom:\n  count: 4\n  strict: true\n  text: '4'\n"));
        _documents.Add(ConfigurationReader.ReadText("morphir.toml", "[project]\nname = 'Acme.Orders'\nsource_directory = 'src'\nexposed_modules = ['Orders.Api']\n[custom]\ncount = 4\nstrict = true\ntext = '4'\n"));
    }

    [When("those documents are read independently")]
    public void ReadIndependently() => _documents.Should().HaveCount(3);

    [Then("the canonical project values are equal with distinct source identities")]
    public void EqualValues()
    {
        var documents = _documents.Select(item => ((SourceContent.Available)item.Content).Item).ToArray();
        documents.Select(item => item.Source.Path).Should().OnlyHaveUniqueItems();
        documents[0].Values.Should().Be(documents[1].Values);
        documents[0].Values.Should().Be(documents[2].Values);
    }
}
