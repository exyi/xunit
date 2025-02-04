﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Xunit.Internal;
using Xunit.Sdk;

namespace Xunit.v3;

/// <summary>
/// Base class for running test cases that implement <see cref="IXunitTestCase"/>. Gives an opportunity
/// for derived classes to define their own context class.
/// </summary>
/// <typeparam name="TContext">The context type used by the runner</typeparam>
public class XunitTestCaseRunnerBase<TContext> : TestCaseRunner<TContext, IXunitTestCase>
	where TContext : XunitTestCaseRunnerContext
{
	/// <summary>
	/// Computes values from the test case and resolves the test method arguments. To be called by the public RunAsync method that
	/// will end up being exposed by the derived class as the primary public API.
	/// </summary>
	/// <param name="testCase">The test case that is being run</param>
	/// <param name="testMethodArguments">The test method arguments to be converted</param>
	protected (Type TestClass, MethodInfo TestMethod, IReadOnlyCollection<BeforeAfterTestAttribute> BeforeAfterTestAttributes) Initialize(
		IXunitTestCase testCase,
		ref object?[]? testMethodArguments)
	{
		var testClass = testCase.TestMethod?.TestClass.Class.ToRuntimeType() ?? throw new ArgumentException("testCase.TestMethod.TestClass.Class does not map to a Type object", nameof(testCase));
		var testMethod = testCase.Method.ToRuntimeMethod() ?? throw new ArgumentException("testCase.TestMethod does not map to a MethodInfo object", nameof(testCase));

		var parameters = testMethod.GetParameters();
		var parameterTypes = new Type[parameters.Length];
		for (var i = 0; i < parameters.Length; i++)
			parameterTypes[i] = parameters[i].ParameterType;

		testMethodArguments = Reflector.ConvertArguments(testMethodArguments, parameterTypes);

		IEnumerable<Attribute> beforeAfterTestCollectionAttributes;

		if (testCase.TestCollection.CollectionDefinition is _IReflectionTypeInfo collectionDefinition)
			beforeAfterTestCollectionAttributes = collectionDefinition.Type.GetCustomAttributes(typeof(BeforeAfterTestAttribute));
		else
			beforeAfterTestCollectionAttributes = Enumerable.Empty<Attribute>();

		var beforeAfterTestAttributes =
			beforeAfterTestCollectionAttributes
				.Concat(testClass.GetCustomAttributes(typeof(BeforeAfterTestAttribute)))
				.Concat(testMethod.GetCustomAttributes(typeof(BeforeAfterTestAttribute)))
				.Concat(testClass.Assembly.GetCustomAttributes(typeof(BeforeAfterTestAttribute)))
				.Cast<BeforeAfterTestAttribute>()
				.CastOrToReadOnlyCollection();

		return (testClass, testMethod, beforeAfterTestAttributes);
	}

	/// <summary>
	/// Creates the <see cref="_ITest"/> instance for the given test case. By default, creates an instance
	/// of the <see cref="XunitTest"/> class.
	/// </summary>
	/// <param name="ctxt">The context that describes the current test case</param>
	/// <param name="displayName">The display name for the test; if <c>null</c>is passed, defaults to
	/// the DisplayName value from <paramref name="ctxt"/>.</param>
	/// <param name="testIndex">The test index for the test. Multiple test per test case scenarios will need
	/// to use the test index to help construct the test unique ID.</param>
	protected virtual _ITest CreateTest(
		TContext ctxt,
		string? displayName,
		int testIndex) =>
			new XunitTest(ctxt.TestCase, displayName ?? ctxt.DisplayName, testIndex);

	/// <inheritdoc/>
	protected override ValueTask<RunSummary> RunTestAsync(TContext ctxt) =>
		XunitTestRunner.Instance.RunAsync(
			CreateTest(ctxt, null, testIndex: 0),
			ctxt.MessageBus,
			ctxt.TestClass,
			ctxt.ConstructorArguments,
			ctxt.TestMethod,
			ctxt.TestMethodArguments,
			ctxt.SkipReason,
			ctxt.Aggregator,
			ctxt.CancellationTokenSource,
			ctxt.BeforeAfterTestAttributes
		);
}
