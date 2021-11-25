using System;
using System.Reflection;
using System.Threading;
using Xunit.Sdk;

namespace Xunit.v3;

/// <summary>
/// Base context record for <see cref="TestInvoker{TContext}"/>
/// </summary>
public record TestInvokerContext(
	_ITest Test,
	Type TestClass,
	object?[] ConstructorArguments,
	MethodInfo TestMethod,
	object?[]? TestMethodArguments,
	IMessageBus MessageBus,
	ExceptionAggregator Aggregator,
	CancellationTokenSource CancellationTokenSource
);
