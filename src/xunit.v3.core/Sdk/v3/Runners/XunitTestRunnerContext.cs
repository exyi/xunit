using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Xunit.Sdk;

namespace Xunit.v3;

/// <summary>
/// Context record for <see cref="XunitTestRunner"/>.
/// </summary>
public record XunitTestRunnerContext : TestRunnerContext
{
	/// <summary>
	/// Initializes a new instance of the <see cref="XunitTestRunnerContext"/> record.
	/// </summary>
	public XunitTestRunnerContext(
		_ITest test,
		IMessageBus messageBus,
		Type testClass,
		object?[] constructorArguments,
		MethodInfo testMethod,
		object?[]? testMethodArguments,
		string? skipReason,
		ExceptionAggregator aggregator,
		CancellationTokenSource cancellationTokenSource,
		IReadOnlyCollection<BeforeAfterTestAttribute> beforeAfterTestAttributes) :
			base(test, messageBus, testClass, constructorArguments, testMethod, testMethodArguments, skipReason, aggregator, cancellationTokenSource)
	{
		BeforeAfterTestAttributes = beforeAfterTestAttributes;
	}

	/// <summary>
	/// Gets or sets the collection of <see cref="BeforeAfterTestAttribute"/> used for this test.
	/// </summary>
	public IReadOnlyCollection<BeforeAfterTestAttribute> BeforeAfterTestAttributes { get; init; }
}
