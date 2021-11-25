using System;
using System.Reflection;
using System.Threading;
using Xunit.Sdk;

namespace Xunit.v3;

/// <summary>
/// Base context record for <see cref="TestRunner{TContext}"/>
/// </summary>
public record TestRunnerContext(
	_ITest Test,
	IMessageBus MessageBus,
	Type TestClass,
	object?[] ConstructorArguments,
	MethodInfo TestMethod,
	object?[]? TestMethodArguments,
	string? SkipReason,
	ExceptionAggregator Aggregator,
	CancellationTokenSource CancellationTokenSource
);
