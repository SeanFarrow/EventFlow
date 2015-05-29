﻿// The MIT License (MIT)
//
// Copyright (c) 2015 Rasmus Mikkelsen
// https://github.com/rasmus/EventFlow
//
// Permission is hereby granted, free of charge, to any person obtaining a copy of
// this software and associated documentation files (the "Software"), to deal in
// the Software without restriction, including without limitation the rights to
// use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of
// the Software, and to permit persons to whom the Software is furnished to do so,
// subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS
// FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
// COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER
// IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN
// CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EventFlow.Aggregates;
using EventFlow.Configuration;
using EventFlow.EventStores;
using EventFlow.ReadStores;
using EventFlow.TestHelpers;
using EventFlow.TestHelpers.Aggregates.Test;
using EventFlow.TestHelpers.Aggregates.Test.Events;
using Moq;
using NUnit.Framework;

namespace EventFlow.Tests.UnitTests.ReadStores
{
    [Timeout(5000)]
    public class ReadModelPopulatorTests : TestsFor<ReadModelPopulator>
    {
        public class TestReadModel : IReadModel,
            IAmReadModelFor<TestAggregate, TestId, PingEvent>
        {
            public void Apply(IReadModelContext context, IDomainEvent<TestAggregate, TestId, PingEvent> e)
            {
            }
        }

        private Mock<IReadModelStore<TestReadModel>> _readModelStoreMock;
        private Mock<IReadStoreManager<TestReadModel>> _readStoreManagerMock;
        private Mock<IEventFlowConfiguration> _eventFlowConfigurationMock;
        private Mock<IEventStore> _eventStoreMock;
        private Mock<IResolver> _resolverMock;
        private List<IDomainEvent> _eventStoreData;

        [SetUp]
        public void SetUp()
        {
            _eventStoreMock = InjectMock<IEventStore>();
            _eventStoreData = null;
            _resolverMock = InjectMock<IResolver>();
            _readModelStoreMock = new Mock<IReadModelStore<TestReadModel>>();
            _readStoreManagerMock = new Mock<IReadStoreManager<TestReadModel>>();
            _eventFlowConfigurationMock = InjectMock<IEventFlowConfiguration>();

            _resolverMock
                .Setup(r => r.Resolve<IEnumerable<IReadStoreManager>>())
                .Returns(new[] {_readStoreManagerMock.Object});
            _resolverMock
                .Setup(r => r.Resolve<IEnumerable<IReadModelStore<TestReadModel>>>())
                .Returns(new[] {_readModelStoreMock.Object});

            _eventFlowConfigurationMock
                .Setup(c => c.PopulateReadModelEventPageSize)
                .Returns(3);

            _eventStoreMock
                .Setup(s => s.LoadAllEventsAsync(It.IsAny<long>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
                .Returns<long, long, CancellationToken>((s, p, c) => Task.FromResult(GetEvents(s, p)));
        }

        [Test]
        public async Task PurgeIsCalled()
        {
            // Act
            await Sut.PurgeAsync<TestReadModel>(CancellationToken.None).ConfigureAwait(false);

            // Assert
            _readModelStoreMock.Verify(s => s.DeleteAllAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task PopulateCallsApplyDomainEvents()
        {
            // Arrange
            ArrangeEventStore(Many<PingEvent>(6));

            // Act
            await Sut.PopulateAsync<TestReadModel>(CancellationToken.None).ConfigureAwait(false);

            // Assert
            _readStoreManagerMock.Verify(
                s => s.UpdateReadStoresAsync(
                    It.Is<IReadOnlyCollection<IDomainEvent>>(l => l.Count == 3),
                    It.IsAny<CancellationToken>()),
                Times.Exactly(2));
        }

        [Test]
        public async Task UnwantedEventsAreFiltered()
        {
            // Arrange
            var events = new IAggregateEvent[]
                {
                    A<PingEvent>(),
                    A<DomainErrorAfterFirstEvent>(),
                    A<PingEvent>(),
                };
            ArrangeEventStore(events);

            // Act
            await Sut.PopulateAsync<TestReadModel>(CancellationToken.None).ConfigureAwait(false);

            // Assert
            _readStoreManagerMock
                .Verify(
                    s => s.UpdateReadStoresAsync(
                        It.Is<IReadOnlyCollection<IDomainEvent>>(l => l.Count == 2 && l.All(e => e.EventType == typeof(PingEvent))),
                        It.IsAny<CancellationToken>()),
                    Times.Once);
        }

        private AllEventsPage GetEvents(long startPosition, long pageSize)
        {
            var events = _eventStoreData
                .Skip((int) Math.Max(startPosition - 1, 0))
                .Take((int)pageSize)
                .ToList();
            var nextPosition = Math.Min(Math.Max(startPosition, 1) + pageSize, _eventStoreData.Count + 1);

            return new AllEventsPage(nextPosition, events);
        }

        private void ArrangeEventStore(IEnumerable<IAggregateEvent> aggregateEvents)
        {
            ArrangeEventStore(aggregateEvents.Select(e => ToDomainEvent(e)));
        }

        private void ArrangeEventStore(IEnumerable<IDomainEvent> domainEvents)
        {
            _eventStoreData = domainEvents.ToList();
        }
    }
}