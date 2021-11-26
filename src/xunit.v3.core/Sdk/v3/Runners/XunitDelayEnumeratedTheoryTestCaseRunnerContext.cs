using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Xunit.Sdk;

namespace Xunit.v3;

/// <summary>
/// Context record for <see cref="XunitDelayEnumeratedTheoryTestCaseRunner"/>.
/// </summary>
public record XunitDelayEnumeratedTheoryTestCaseRunnerContext : XunitTestCaseRunnerContext
{
	/// <summary>
	/// Initializes a new instance of the <see cref="XunitDelayEnumeratedTheoryTestCaseRunnerContext"/> record.
	/// </summary>
	public XunitDelayEnumeratedTheoryTestCaseRunnerContext(
		IXunitTestCase testCase,
		IMessageBus messageBus,
		ExceptionAggregator aggregator,
		CancellationTokenSource cancellationTokenSource,
		string displayName,
		string? skipReason,
		Type testClass,
		object?[] constructorArguments,
		MethodInfo testMethod,
		object?[]? testMethodArguments,
		IReadOnlyCollection<BeforeAfterTestAttribute> beforeAfterTestAttributes,
		_IMessageSink diagnosticMessageSink) :
			base(testCase, messageBus, aggregator, cancellationTokenSource, displayName, skipReason, testClass, constructorArguments, testMethod, testMethodArguments, beforeAfterTestAttributes)
	{
		DiagnosticMessageSink = diagnosticMessageSink;
	}

	/// <summary>
	/// An exception aggregator that is used to store exceptions that should be surfaced as cleanup failures.
	/// </summary>
	public ExceptionAggregator CleanupAggregator { get; } = new();

	/// <summary>
	/// Gets the message sink that should be used for sending diagnostic messages.
	/// </summary>
	public _IMessageSink DiagnosticMessageSink { get; init; }

	/// <summary>
	/// Gets a container to place tests found during discovery.
	/// </summary>
	public List<(_ITest Test, MethodInfo TestMethod, object?[]? TestMethodArguments, string? SkipReason)> DiscoveredTests { get; } = new();

	/// <summary>
	/// Gets or sets the exception that occurred during data discovery. When this is set, running the test case will result in
	/// a single failure related to the discovery exception.
	/// </summary>
	public Exception? DiscoveryException { get; set; }

	/// <summary>
	/// Gets the disposal tracker used to ensure that discovered data is properly disposed of.
	/// </summary>
	public DisposalTracker DisposalTracker { get; } = new();
}
