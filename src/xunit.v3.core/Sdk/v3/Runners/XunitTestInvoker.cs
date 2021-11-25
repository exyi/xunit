using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Xunit.Internal;
using Xunit.Sdk;

namespace Xunit.v3;

/// <summary>
/// The test invoker for xUnit.net v3 tests.
/// </summary>
public class XunitTestInvoker : TestInvoker
{
	// This dictionary holds instances of the before/after test attributes by test ID. This is initially
	// populated by the call to RunAsync() with the complete list of test attributes; in BeforeTestMethodInvokedAsync,
	// they are removed from the dictionary, and attempted to be run. The list of attributes that were successfully
	// run is then put back into the dictionary, to be removed by AfterTestMethodInvokedAsync and then called
	// during cleanup.
	ConcurrentDictionary<string, IReadOnlyCollection<BeforeAfterTestAttribute>> beforeAfterTestAttributesByTestID = new();

	/// <summary>
	/// Initializes a new instance of the <see cref="XunitTestInvoker"/> class.
	/// </summary>
	protected XunitTestInvoker()
	{ }

	/// <summary>
	/// Gets the singleton instance of the <see cref="XunitTestInvoker"/>.
	/// </summary>
	public static XunitTestInvoker Instance = new();

	/// <inheritdoc/>
	protected override ValueTask AfterTestMethodInvokedAsync(
		_ITest test,
		Type testClass,
		MethodInfo testMethod,
		IMessageBus messageBus,
		ExceptionAggregator aggregator,
		CancellationTokenSource cancellationTokenSource)
	{
		var testUniqueID = test.UniqueID;

		// At this point, this list has been pruned to only the attributes that were successfully run
		// during the call to BeforeTestMethodInvokedAsync.
		if (beforeAfterTestAttributesByTestID.TryRemove(testUniqueID, out var beforeAfterTestAttributes) && beforeAfterTestAttributes.Count > 0)
		{
			var testAssemblyUniqueID = test.TestCase.TestCollection.TestAssembly.UniqueID;
			var testCollectionUniqueID = test.TestCase.TestCollection.UniqueID;
			var testClassUniqueID = test.TestCase.TestMethod?.TestClass.UniqueID;
			var testMethodUniqueID = test.TestCase.TestMethod?.UniqueID;
			var testCaseUniqueID = test.TestCase.UniqueID;

			foreach (var beforeAfterAttribute in beforeAfterTestAttributes)
			{
				var attributeName = beforeAfterAttribute.GetType().Name;
				var afterTestStarting = new _AfterTestStarting
				{
					AssemblyUniqueID = testAssemblyUniqueID,
					AttributeName = attributeName,
					TestCaseUniqueID = testCaseUniqueID,
					TestClassUniqueID = testClassUniqueID,
					TestCollectionUniqueID = testCollectionUniqueID,
					TestMethodUniqueID = testMethodUniqueID,
					TestUniqueID = testUniqueID
				};

				if (!messageBus.QueueMessage(afterTestStarting))
					cancellationTokenSource.Cancel();

				aggregator.Run(() => beforeAfterAttribute.After(testMethod, test));

				var afterTestFinished = new _AfterTestFinished
				{
					AssemblyUniqueID = testAssemblyUniqueID,
					AttributeName = attributeName,
					TestCaseUniqueID = testCaseUniqueID,
					TestClassUniqueID = testClassUniqueID,
					TestCollectionUniqueID = testCollectionUniqueID,
					TestMethodUniqueID = testMethodUniqueID,
					TestUniqueID = testUniqueID
				};

				if (!messageBus.QueueMessage(afterTestFinished))
					cancellationTokenSource.Cancel();
			}
		}

		return default;
	}

	/// <inheritdoc/>
	protected override ValueTask BeforeTestMethodInvokedAsync(
		_ITest test,
		Type testClass,
		MethodInfo testMethod,
		IMessageBus messageBus,
		ExceptionAggregator aggregator,
		CancellationTokenSource cancellationTokenSource)
	{
		var testUniqueID = test.UniqueID;

		// At this point, this list is the full attribute list from the call to RunAsync. We attempt to
		// run the Before half of the attributes, and then keep track of which ones we successfully ran,
		// so we can put only those back into the dictionary for later retrieval by AfterTestMethodInvokedAsync.
		if (beforeAfterTestAttributesByTestID.TryRemove(testUniqueID, out var beforeAfterTestAttributes) && beforeAfterTestAttributes.Count > 0)
		{
			var testAssemblyUniqueID = test.TestCase.TestCollection.TestAssembly.UniqueID;
			var testCollectionUniqueID = test.TestCase.TestCollection.UniqueID;
			var testClassUniqueID = test.TestCase.TestMethod?.TestClass.UniqueID;
			var testMethodUniqueID = test.TestCase.TestMethod?.UniqueID;
			var testCaseUniqueID = test.TestCase.UniqueID;

			// Since we want them cleaned in reverse order from how they're run, we'll push a stack back
			// into the container rather than a list.
			var beforeAfterAttributesRun = new Stack<BeforeAfterTestAttribute>();

			foreach (var beforeAfterAttribute in beforeAfterTestAttributes)
			{
				var attributeName = beforeAfterAttribute.GetType().Name;
				var beforeTestStarting = new _BeforeTestStarting
				{
					AssemblyUniqueID = testAssemblyUniqueID,
					AttributeName = attributeName,
					TestCaseUniqueID = testCaseUniqueID,
					TestClassUniqueID = testClassUniqueID,
					TestCollectionUniqueID = testCollectionUniqueID,
					TestMethodUniqueID = testMethodUniqueID,
					TestUniqueID = testUniqueID
				};

				if (!messageBus.QueueMessage(beforeTestStarting))
					cancellationTokenSource.Cancel();
				else
				{
					try
					{
						beforeAfterAttribute.Before(testMethod, test);
						beforeAfterAttributesRun.Push(beforeAfterAttribute);
					}
					catch (Exception ex)
					{
						aggregator.Add(ex);
						break;
					}
					finally
					{
						var beforeTestFinished = new _BeforeTestFinished
						{
							AssemblyUniqueID = testAssemblyUniqueID,
							AttributeName = attributeName,
							TestCaseUniqueID = testCaseUniqueID,
							TestClassUniqueID = testClassUniqueID,
							TestCollectionUniqueID = testCollectionUniqueID,
							TestMethodUniqueID = testMethodUniqueID,
							TestUniqueID = testUniqueID
						};

						if (!messageBus.QueueMessage(beforeTestFinished))
							cancellationTokenSource.Cancel();
					}
				}

				if (cancellationTokenSource.IsCancellationRequested)
					break;
			}

			beforeAfterTestAttributesByTestID[testUniqueID] = beforeAfterAttributesRun;
		}

		return default;
	}

	/// <inheritdoc/>
	protected override object? CreateTestClassInstance(
		_ITest test,
		Type testClass,
		object?[] constructorArguments)
	{
		// We allow for Func<T> when the argument is T, such that we should be able to get the value just before
		// invoking the test. So we need to do a transform on the arguments.
		object?[]? actualCtorArguments = null;

		if (constructorArguments != null)
		{
			var ctorParams =
				testClass
					.GetConstructors()
					.Where(ci => !ci.IsStatic && ci.IsPublic)
					.Single()
					.GetParameters();

			actualCtorArguments = new object?[constructorArguments.Length];

			for (var idx = 0; idx < constructorArguments.Length; ++idx)
			{
				actualCtorArguments[idx] = constructorArguments[idx];

				var ctorArgumentValueType = constructorArguments[idx]?.GetType();
				if (ctorArgumentValueType != null)
				{
					var ctorArgumentParamType = ctorParams[idx].ParameterType;
					if (ctorArgumentParamType != ctorArgumentValueType &&
						ctorArgumentValueType == typeof(Func<>).MakeGenericType(ctorArgumentParamType))
					{
						var invokeMethod = ctorArgumentValueType.GetMethod("Invoke", new Type[0]);
						if (invokeMethod != null)
							actualCtorArguments[idx] = invokeMethod.Invoke(constructorArguments[idx], new object?[0]);
					}
				}
			}
		}

		return Activator.CreateInstance(testClass, actualCtorArguments);
	}

	/// <inheritdoc/>
	protected override ValueTask<decimal> InvokeTestMethodAsync(
		_ITest test,
		object? testClassInstance,
		MethodInfo testMethod,
		object?[]? testMethodArguments,
		ExceptionAggregator aggregator)
	{
		var testCase = (IXunitTestCase)test.TestCase;

		if (testCase.InitializationException != null)
		{
			var tcs = new TaskCompletionSource<decimal>();
			tcs.SetException(testCase.InitializationException);
			return new(tcs.Task);
		}

		return
			testCase.Timeout > 0
				? InvokeTimeoutTestMethodAsync(testCase.Timeout, test, testClassInstance, testMethod, testMethodArguments, aggregator)
				: base.InvokeTestMethodAsync(test, testClassInstance, testMethod, testMethodArguments, aggregator);
	}

	async ValueTask<decimal> InvokeTimeoutTestMethodAsync(
		int timeout,
		_ITest test,
		object? testClassInstance,
		MethodInfo testMethod,
		object?[]? testMethodArguments,
		ExceptionAggregator aggregator)
	{
		if (!testMethod.IsAsync())
			throw TestTimeoutException.ForIncompatibleTest();

		var baseTask = base.InvokeTestMethodAsync(test, testClassInstance, testMethod, testMethodArguments, aggregator).AsTask();
		var resultTask = await Task.WhenAny(baseTask, Task.Delay(timeout));

		if (resultTask != baseTask)
			throw TestTimeoutException.ForTimedOutTest(timeout);

		return await baseTask;
	}

	/// <summary>
	/// Creates the test class (if necessary), and invokes the test method.
	/// </summary>
	/// <param name="test">The test that should be run</param>
	/// <param name="testClass">The type that the test belongs to</param>
	/// <param name="constructorArguments">The constructor arguments used to create the class instance</param>
	/// <param name="testMethod">The method that the test belongs to</param>
	/// <param name="testMethodArguments">The arguments for the test method</param>
	/// <param name="beforeAfterTestAttributes">The <see cref="BeforeAfterTestAttribute"/> instances attached to this test</param>
	/// <param name="messageBus">The message bus to send execution messages to</param>
	/// <param name="aggregator">The aggregator used to </param>
	/// <param name="cancellationTokenSource">The cancellation token source used to cancel test execution</param>
	/// <returns>Returns the time (in seconds) spent creating the test class, running
	/// the test, and disposing of the test class.</returns>
	public ValueTask<decimal> RunAsync(
		_ITest test,
		Type testClass,
		object?[] constructorArguments,
		MethodInfo testMethod,
		object?[]? testMethodArguments,
		IReadOnlyCollection<BeforeAfterTestAttribute> beforeAfterTestAttributes,
		IMessageBus messageBus,
		ExceptionAggregator aggregator,
		CancellationTokenSource cancellationTokenSource)
	{
		if (beforeAfterTestAttributes != null && beforeAfterTestAttributes.Count > 0)
			beforeAfterTestAttributesByTestID[test.UniqueID] = beforeAfterTestAttributes;

		return base.RunAsync(test, testClass, constructorArguments, testMethod, testMethodArguments, messageBus, aggregator, cancellationTokenSource);
	}
}
