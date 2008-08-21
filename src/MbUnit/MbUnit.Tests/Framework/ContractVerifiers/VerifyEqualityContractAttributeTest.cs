using System;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.Serialization;
using Gallio.Model;
using Gallio.Runner.Reports;
using Gallio.Tests.Integration;
using MbUnit.Framework;
using MbUnit.Framework.ContractVerifiers;

namespace MbUnit.Tests.Framework.ContractVerifiers
{
    [TestFixture]
    [TestsOn(typeof(VerifyEqualityContractAttribute))]
    public class VerifyEqualityContractAttributeTest : VerifyContractAttributeBaseTest
    {
        [Test]
        [Row(typeof(FullContractOnSampleEquatableTest), "ObjectEquals", TestStatus.Passed)]
        [Row(typeof(FullContractOnSampleEquatableTest), "ObjectGetHashCode", TestStatus.Passed)]
        [Row(typeof(FullContractOnSampleEquatableTest), "EquatableEquals", TestStatus.Passed)]
        [Row(typeof(FullContractOnSampleEquatableTest), "OperatorEquals", TestStatus.Passed)]
        [Row(typeof(FullContractOnSampleEquatableTest), "OperatorNotEquals", TestStatus.Passed)]
        [Row(typeof(PartialContractOnSampleEquatableTest), "ObjectEquals", TestStatus.Passed)]
        [Row(typeof(PartialContractOnSampleEquatableTest), "ObjectGetHashCode", TestStatus.Passed)]
        [Row(typeof(PartialContractOnSampleEquatableTest), "EquatableEquals", TestStatus.Passed)]
        [Row(typeof(PartialContractOnSampleEquatableTest), "OperatorEquals", TestStatus.Inconclusive)]
        [Row(typeof(PartialContractOnSampleEquatableTest), "OperatorNotEquals", TestStatus.Inconclusive)]
        public void VerifySampleEqualityContract(Type fixtureType, string testMethodName, TestStatus expectedTestStatus)
        {
            VerifySampleContract("EqualityContract", fixtureType, testMethodName, expectedTestStatus);
        }

        [VerifyEqualityContract(typeof(SampleEquatable),
            ImplementsOperatorOverloads = true),
        Explicit]
        private class FullContractOnSampleEquatableTest : IEquivalenceClassProvider<SampleEquatable>
        {
            public EquivalenceClassCollection<SampleEquatable> GetEquivalenceClasses()
            {
                return EquivalenceClassCollection<SampleEquatable>.FromDistinctInstances(
                    new SampleEquatable(123),
                    new SampleEquatable(456),
                    new SampleEquatable(789));
            }
        }

        [VerifyEqualityContract(typeof(SampleEquatable),
            ImplementsOperatorOverloads = false),
        Explicit]
        private class PartialContractOnSampleEquatableTest : IEquivalenceClassProvider<SampleEquatable>
        {
            public EquivalenceClassCollection<SampleEquatable> GetEquivalenceClasses()
            {
                return EquivalenceClassCollection<SampleEquatable>.FromDistinctInstances(
                    new SampleEquatable(123),
                    new SampleEquatable(456),
                    new SampleEquatable(789));
            }
        }

        /// <summary>
        /// Sample equatable type.
        /// </summary>
        internal class SampleEquatable : IEquatable<SampleEquatable>
        {
            private int value;

            public SampleEquatable(int value)
            {
                this.value = value;
            }

            public override int GetHashCode()
            {
                return value.GetHashCode();
            }

            public override bool Equals(object other)
            {
                return Equals(other as SampleEquatable);
            }

            public bool Equals(SampleEquatable other)
            {
                return (other != null) && (value == other.value);
            }

            public static bool operator ==(SampleEquatable left, SampleEquatable right)
            {
                return
                    (((object)left == null) && ((object)right == null)) ||
                    (((object)left != null) && left.Equals(right));
            }

            public static bool operator !=(SampleEquatable left, SampleEquatable right)
            {
                return !(left == right);
            }
        }
    }
}