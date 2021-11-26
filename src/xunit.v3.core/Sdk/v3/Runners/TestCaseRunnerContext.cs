using System.Threading;
using Xunit.Sdk;

namespace Xunit.v3;

/// <summary>
/// Base context record for <see cref="TestCaseRunner{TContext, TTestCase}"/>
/// </summary>
public record TestCaseRunnerContext<TTestCase>(
	TTestCase TestCase,
	IMessageBus MessageBus,
	ExceptionAggregator Aggregator,
	CancellationTokenSource CancellationTokenSource
) where TTestCase : _ITestCase;
