using LLama;
using LLama.Native;
using LLama.Sampling;

namespace LocalLizard.LocalLLM;

/// <summary>
/// Custom sampling pipeline that uses lazy grammar for tool calling.
/// Before the trigger pattern (&lt;|tool_call|&gt;) is matched, free text flows
/// normally. After trigger, the grammar constrains to valid tool call format.
/// </summary>
public sealed class LazyToolGrammarPipeline : BaseSamplingPipeline
{
    private readonly string _gbnfGrammar;
    private readonly string _rootRule;
    private readonly string[] _triggerPatterns;
    private readonly LLamaToken[] _triggerTokens;

    public LazyToolGrammarPipeline(
        SafeLlamaModelHandle model,
        string gbnfGrammar,
        string rootRule = "root",
        string[]? triggerPatterns = null,
        LLamaToken[]? triggerTokens = null,
        float temperature = 0.0f)
    {
        _gbnfGrammar = gbnfGrammar;
        _rootRule = rootRule;
        _triggerPatterns = triggerPatterns ?? [];
        _triggerTokens = triggerTokens ?? [];
        _model = model;
        _temperature = temperature;
    }

    private readonly SafeLlamaModelHandle _model;
    private readonly float _temperature;

    protected override SafeLLamaSamplerChainHandle CreateChain(SafeLLamaContextHandle context)
    {
        var chain = SafeLLamaSamplerChainHandle.Create(LLamaSamplerChainParams.Default());

        if (_temperature <= 0.0f)
            chain.AddGreedySampler();
        else
            chain.AddTemperature(_temperature);

        // Add lazy grammar — triggers on pattern match or trigger tokens
        chain.AddLazyGrammar(
            _model,
            _gbnfGrammar,
            _rootRule,
            _triggerPatterns,
            _triggerTokens);

        return chain;
    }
}
