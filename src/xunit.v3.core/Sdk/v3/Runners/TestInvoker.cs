using System;
using System.Linq;
using System.Reflection;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using Xunit.Internal;
using Xunit.Sdk;

namespace Xunit.v3;

/// <summary>
/// A base class that provides default behavior to invoke a test method. This includes
/// support for async test methods ("async Task", "async ValueTask", and "async void" for C#/VB,
/// and async functions in F#) as well as creation and disposal of the test class. This class
/// is designed to be a singleton for performance reasons.
/// </summary>
public abstract class TestInvoker
{
	static MethodInfo? fSharpStartAsTaskOpenGenericMethod;

	/// <summary>
	/// This method is called just after the test method has finished executing. This method should NEVER throw;
	/// any exceptions should be placed into the <paramref name="aggregator"/>.
	/// </summary>
	/// <param name="test">The test that just finished running</param>
	/// <param name="testClass">The type that the test belongs to</param>
	/// <param name="testMethod">The method that the test belongs to</param>
	/// <param name="messageBus">The message bus to send execution messages to</param>
	/// <param name="aggregator">The aggregator used to record exceptions</param>
	/// <param name="cancellationTokenSource">The cancellation token source used to cancel test execution</param>
	protected virtual ValueTask AfterTestMethodInvokedAsync(
		_ITest test,
		Type testClass,
		MethodInfo testMethod,
		IMessageBus messageBus,
		ExceptionAggregator aggregator,
		CancellationTokenSource cancellationTokenSource)
			=> default;

	/// <summary>
	/// This method is called just before the test method is invoked. This method should NEVER throw;
	/// any exceptions should be placed into the <paramref name="aggregator"/>.
	/// </summary>
	/// <param name="test">The test that is about to be run</param>
	/// <param name="testClass">The type that the test belongs to</param>
	/// <param name="testMethod">The method that the test belongs to</param>
	/// <param name="messageBus">The message bus to send execution messages to</param>
	/// <param name="aggregator">The aggregator used to record exceptions</param>
	/// <param name="cancellationTokenSource">The cancellation token source used to cancel test execution</param>
	protected virtual ValueTask BeforeTestMethodInvokedAsync(
		_ITest test,
		Type testClass,
		MethodInfo testMethod,
		IMessageBus messageBus,
		ExceptionAggregator aggregator,
		CancellationTokenSource cancellationTokenSource)
			=> default;

	/// <summary>
	/// This method calls the test method via <see cref="MethodBase.Invoke(object, object[])"/>. This is an available override
	/// point if you need to do some other form of invocation of the actual test method.
	/// </summary>
	/// <param name="testClassInstance">The instance of the test class</param>
	/// <param name="testMethod">The test method to be invoked</param>
	/// <param name="testMethodArguments">The arguments for the test method</param>
	/// <returns>The return value from the test method invocation</returns>
	protected virtual object? CallTestMethod(
		object? testClassInstance,
		MethodInfo testMethod,
		object?[]? testMethodArguments) =>
			testMethod.Invoke(testClassInstance, testMethodArguments);

	/// <summary>
	/// Creates the test class, unless the test method is static or there have already been errors. Note that
	/// this method times the creation of the test class (using <see cref="Timer"/>). It is also responsible for
	/// sending the <see cref="_TestClassConstructionStarting"/> and <see cref="_TestClassConstructionFinished"/>
	/// messages, so if you override this method without calling the base, you are responsible for all of this behavior.
	/// This method should NEVER throw; any exceptions should be placed into the <paramref name="aggregator"/>. To override
	/// just the behavior of creating the instance of the test class, override <see cref="CreateTestClassInstance"/>
	/// instead.
	/// </summary>
	/// <param name="test">The test that is being run</param>
	/// <param name="testClass">The type that the test belongs to</param>
	/// <param name="constructorArguments">The constructor arguments used to create the class instance</param>
	/// <param name="testMethod">The method that the test belongs to</param>
	/// <param name="messageBus">The message bus to send execution messages to</param>
	/// <param name="aggregator">The aggregator used to record exceptions</param>
	/// <param name="cancellationTokenSource">The cancellation token source used to cancel test execution</param>
	/// <returns>A tuple which includes the class instance (<c>null</c> if one was not created) as well as the elapsed
	/// time spent creating the class</returns>
	protected virtual object? CreateTestClass(
		_ITest test,
		Type testClass,
		object?[] constructorArguments,
		MethodInfo testMethod,
		IMessageBus messageBus,
		ExceptionAggregator aggregator,
		CancellationTokenSource cancellationTokenSource)
	{
		return aggregator.Run(() =>
		{
			if (!testMethod.IsStatic && !aggregator.HasExceptions)
			{
				var testAssemblyUniqueID = test.TestCase.TestCollection.TestAssembly.UniqueID;
				var testCollectionUniqueID = test.TestCase.TestCollection.UniqueID;
				var testClassUniqueID = test.TestCase.TestMethod?.TestClass.UniqueID;
				var testMethodUniqueID = test.TestCase.TestMethod?.UniqueID;
				var testCaseUniqueID = test.TestCase.UniqueID;
				var testUniqueID = test.UniqueID;

				var testClassConstructionStarting = new _TestClassConstructionStarting
				{
					AssemblyUniqueID = testAssemblyUniqueID,
					TestCaseUniqueID = testCaseUniqueID,
					TestClassUniqueID = testClassUniqueID,
					TestCollectionUniqueID = testCollectionUniqueID,
					TestMethodUniqueID = testMethodUniqueID,
					TestUniqueID = testUniqueID
				};

				if (!messageBus.QueueMessage(testClassConstructionStarting))
					cancellationTokenSource.Cancel();
				else
				{
					try
					{
						if (!cancellationTokenSource.IsCancellationRequested)
							return aggregator.Run(() => CreateTestClassInstance(test, testClass, constructorArguments));
					}
					finally
					{
						var testClassConstructionFinished = new _TestClassConstructionFinished
						{
							AssemblyUniqueID = testAssemblyUniqueID,
							TestCaseUniqueID = testCaseUniqueID,
							TestClassUniqueID = testClassUniqueID,
							TestCollectionUniqueID = testCollectionUniqueID,
							TestMethodUniqueID = testMethodUniqueID,
							TestUniqueID = testUniqueID
						};

						if (!messageBus.QueueMessage(testClassConstructionFinished))
							cancellationTokenSource.Cancel();
					}
				}
			}

			return null;
		});
	}

	/// <summary>
	/// Creates the instance of the test class. By default, uses <see cref="Activator.CreateInstance(Type, object[])"/>
	/// with the values from <paramref name="testClass"/> and <paramref name="constructorArguments"/>. You should override
	/// this in order to change the input values and/or use a factory method other than Activator.CreateInstance.
	/// </summary>
	/// <param name="test">The test that is currently being run</param>
	/// <param name="testClass">The <see cref="Type"/> that represents the test class</param>
	/// <param name="constructorArguments">The arguments to send to the test class constructor</param>
	protected virtual object? CreateTestClassInstance(
		_ITest test,
		Type testClass,
		object?[] constructorArguments) =>
			Activator.CreateInstance(testClass, constructorArguments);

	/// <summary>
	/// Given an object, will attempt to convert instances of <see cref="Task"/> or
	/// <see cref="T:Microsoft.FSharp.Control.FSharpAsync`1"/> into <see cref="ValueTask"/>
	/// as appropriate. Will return <c>null</c> if the object is not a task of any supported type.
	/// </summary>
	/// <param name="obj">The object to convert</param>
	protected static ValueTask? GetValueTaskFromResult(object? obj)
	{
		if (obj == null)
			return null;

		if (obj is Task task)
		{
			if (task.Status == TaskStatus.Created)
				throw new InvalidOperationException("Test method returned a non-started Task (tasks must be started before being returned)");

			return new(task);
		}

		if (obj is ValueTask valueTask)
			return valueTask;

		var type = obj.GetType();
		if (type.IsGenericType && type.GetGenericTypeDefinition().FullName == "Microsoft.FSharp.Control.FSharpAsync`1")
		{
			if (fSharpStartAsTaskOpenGenericMethod == null)
			{
				fSharpStartAsTaskOpenGenericMethod =
					type
						.Assembly
						.GetType("Microsoft.FSharp.Control.FSharpAsync")?
						.GetRuntimeMethods()
						.FirstOrDefault(m => m.Name == "StartAsTask");

				if (fSharpStartAsTaskOpenGenericMethod == null)
					throw new InvalidOperationException("Test returned an F# async result, but could not find 'Microsoft.FSharp.Control.FSharpAsync.StartAsTask'");
			}

			if (fSharpStartAsTaskOpenGenericMethod
					.MakeGenericMethod(type.GetGenericArguments()[0])
					.Invoke(null, new[] { obj, null, null }) is Task fsharpTask)
				return new(fsharpTask);
		}

		return null;
	}

	/// <summary>
	/// Invokes the test method on the given test class instance. This method sets up support for "async void"
	/// test methods, ensures that the test method has the correct number of arguments, then calls <see cref="CallTestMethod"/>
	/// to do the actual method invocation. It ensure that any async test method is fully completed before returning, and
	/// returns the measured clock time that the invocation took. This method should NEVER throw; any exceptions should be
	/// placed into the <paramref name="aggregator"/>.
	/// </summary>
	/// <param name="test">The test to be run</param>
	/// <param name="testClassInstance">The test class instance</param>
	/// <param name="testMethod">The test method to be invoked</param>
	/// <param name="testMethodArguments">The arguments for the test method</param>
	/// <param name="aggregator">The aggregator used to record exceptions</param>
	/// <returns>Returns the amount of time the test took to run, in seconds</returns>
	protected virtual async ValueTask<decimal> InvokeTestMethodAsync(
		_ITest test,
		object? testClassInstance,
		MethodInfo testMethod,
		object?[]? testMethodArguments,
		ExceptionAggregator aggregator)
	{
		var oldSyncContext = default(SynchronizationContext);

		try
		{
			var asyncSyncContext = default(AsyncTestSyncContext);

			if (testMethod.IsAsyncVoid())
			{
				oldSyncContext = SynchronizationContext.Current;
				asyncSyncContext = new AsyncTestSyncContext(oldSyncContext);
				SetSynchronizationContext(asyncSyncContext);
			}

			var elapsed = await ExecutionTimer.MeasureAsync(
				() => aggregator.RunAsync(
					async () =>
					{
						var parameterCount = testMethod.GetParameters().Length;
						var valueCount = testMethodArguments == null ? 0 : testMethodArguments.Length;
						if (parameterCount != valueCount)
						{
							aggregator.Add(
								new InvalidOperationException(
									$"The test method expected {parameterCount} parameter value{(parameterCount == 1 ? "" : "s")}, but {valueCount} parameter value{(valueCount == 1 ? "" : "s")} {(valueCount == 1 ? "was" : "were")} provided."
								)
							);
						}
						else
						{
							var result = CallTestMethod(testClassInstance, testMethod, testMethodArguments);
							var valueTask = GetValueTaskFromResult(result);
							if (valueTask.HasValue)
								await valueTask.Value;
							else if (asyncSyncContext != null)
							{
								var ex = await asyncSyncContext.WaitForCompletionAsync();
								if (ex != null)
									aggregator.Add(ex);
							}
						}
					}
				)
			);

			return (decimal)elapsed.TotalSeconds;
		}
		finally
		{
			if (oldSyncContext != null)
				SetSynchronizationContext(oldSyncContext);
		}
	}

	/// <summary>
	/// Creates the test class (if necessary), and invokes the test method.
	/// </summary>
	/// <param name="test">The test that should be run</param>
	/// <param name="testClass">The type that the test belongs to</param>
	/// <param name="constructorArguments">The constructor arguments used to create the class instance</param>
	/// <param name="testMethod">The method that the test belongs to</param>
	/// <param name="testMethodArguments">The arguments for the test method</param>
	/// <param name="messageBus">The message bus to send execution messages to</param>
	/// <param name="aggregator">The aggregator used to </param>
	/// <param name="cancellationTokenSource">The cancellation token source used to cancel test execution</param>
	/// <returns>Returns the time (in seconds) spent creating the test class, running
	/// the test, and disposing of the test class.</returns>
	protected ValueTask<decimal> RunAsync(
		_ITest test,
		Type testClass,
		object?[] constructorArguments,
		MethodInfo testMethod,
		object?[]? testMethodArguments,
		IMessageBus messageBus,
		ExceptionAggregator aggregator,
		CancellationTokenSource cancellationTokenSource)
	{
		return aggregator.RunAsync(async () =>
		{
			Guard.ArgumentNotNull(test);

			if (cancellationTokenSource.IsCancellationRequested)
				return 0m;

			SetTestContext(test, TestEngineStatus.Initializing, cancellationTokenSource.Token);

			object? testClassInstance = null;
			var elapsedTime = ExecutionTimer.Measure(() => { testClassInstance = CreateTestClass(test, testClass, constructorArguments, testMethod, messageBus, aggregator, cancellationTokenSource); });

			var asyncDisposable = testClassInstance as IAsyncDisposable;
			var disposable = testClassInstance as IDisposable;

			var testAssemblyUniqueID = test.TestCase.TestCollection.TestAssembly.UniqueID;
			var testCollectionUniqueID = test.TestCase.TestCollection.UniqueID;
			var testClassUniqueID = test.TestCase.TestMethod?.TestClass.UniqueID;
			var testMethodUniqueID = test.TestCase.TestMethod?.UniqueID;
			var testCaseUniqueID = test.TestCase.UniqueID;
			var testUniqueID = test.UniqueID;

			try
			{
				if (testClassInstance is IAsyncLifetime asyncLifetime)
					elapsedTime += await ExecutionTimer.MeasureAsync(asyncLifetime.InitializeAsync);

				try
				{
					if (!cancellationTokenSource.IsCancellationRequested)
					{
						elapsedTime += await ExecutionTimer.MeasureAsync(() => BeforeTestMethodInvokedAsync(test, testClass, testMethod, messageBus, aggregator, cancellationTokenSource));

						SetTestContext(test, TestEngineStatus.Running, cancellationTokenSource.Token);

						if (!cancellationTokenSource.IsCancellationRequested && !aggregator.HasExceptions)
							await InvokeTestMethodAsync(test, testClassInstance, testMethod, testMethodArguments, aggregator);

						SetTestContext(test, TestEngineStatus.CleaningUp, cancellationTokenSource.Token, TestState.FromException((decimal)elapsedTime.TotalSeconds, aggregator.ToException()));

						elapsedTime += await ExecutionTimer.MeasureAsync(() => AfterTestMethodInvokedAsync(test, testClass, testMethod, messageBus, aggregator, cancellationTokenSource));
					}
				}
				finally
				{
					if (asyncDisposable != null || disposable != null)
					{
						var testClassDisposeStarting = new _TestClassDisposeStarting
						{
							AssemblyUniqueID = testAssemblyUniqueID,
							TestCaseUniqueID = testCaseUniqueID,
							TestClassUniqueID = testClassUniqueID,
							TestCollectionUniqueID = testCollectionUniqueID,
							TestMethodUniqueID = testMethodUniqueID,
							TestUniqueID = testUniqueID
						};

						if (!messageBus.QueueMessage(testClassDisposeStarting))
							cancellationTokenSource.Cancel();
					}

					if (asyncDisposable != null)
						elapsedTime += await ExecutionTimer.MeasureAsync(() => aggregator.RunAsync(asyncDisposable.DisposeAsync));
				}
			}
			finally
			{
				if (disposable != null)
					elapsedTime += ExecutionTimer.Measure(() => aggregator.Run(disposable.Dispose));

				if (asyncDisposable != null || disposable != null)
				{
					var testClassDisposeFinished = new _TestClassDisposeFinished
					{
						AssemblyUniqueID = testAssemblyUniqueID,
						TestCaseUniqueID = testCaseUniqueID,
						TestClassUniqueID = testClassUniqueID,
						TestCollectionUniqueID = testCollectionUniqueID,
						TestMethodUniqueID = testMethodUniqueID,
						TestUniqueID = testUniqueID
					};

					if (!messageBus.QueueMessage(testClassDisposeFinished))
						cancellationTokenSource.Cancel();
				}
			}

			return (decimal)elapsedTime.TotalSeconds;
		});
	}

	[SecuritySafeCritical]
	static void SetSynchronizationContext(SynchronizationContext? context) =>
		SynchronizationContext.SetSynchronizationContext(context);

	/// <summary>
	/// Sets the test context for the given test state and engine status.
	/// </summary>
	/// <param name="test">The test that is currently running</param>
	/// <param name="testStatus">The current engine status for the test</param>
	/// <param name="cancellationToken">The cancellation token which indicates tests should stop running</param>
	/// <param name="testState">The current test state</param>
	protected virtual void SetTestContext(
		_ITest test,
		TestEngineStatus testStatus,
		CancellationToken cancellationToken,
		TestState? testState = null) =>
			TestContext.SetForTest(test, testStatus, cancellationToken, testState);
}
