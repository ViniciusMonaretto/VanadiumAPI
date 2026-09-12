using Data.Mongo;
using Microsoft.Extensions.Logging;
using Moq;
using Shared.Models;
using VanadiumAPI.Services;
using Xunit;

namespace VanadiumAPI.Tests
{
    public class PanelReadingServiceTests
    {
        private readonly Mock<IPanelReadingRepository> _repository = new();

        private PanelReadingService CreateService() =>
            new(_repository.Object, new Mock<ILogger<PanelReadingService>>().Object);

        [Fact]
        public async Task GetPanelReadingsAsync_DelegatesToRepositoryWithSameArguments()
        {
            var start = new DateTime(2026, 1, 1);
            var end = new DateTime(2026, 1, 2);
            var expected = new List<PanelReading> { new() { PanelId = 1, Value = 3.5f } };
            _repository.Setup(r => r.GetPanelReadingsByPanelId(1, start, end)).ReturnsAsync(expected);
            var service = CreateService();

            var result = await service.GetPanelReadingsAsync(1, start, end);

            Assert.Same(expected, result);
        }

        [Fact]
        public async Task GetPanelReadingsAsync_NoDateRange_PassesNullsThrough()
        {
            _repository.Setup(r => r.GetPanelReadingsByPanelId(1, null, null)).ReturnsAsync(new List<PanelReading>());
            var service = CreateService();

            await service.GetPanelReadingsAsync(1);

            _repository.Verify(r => r.GetPanelReadingsByPanelId(1, null, null), Times.Once);
        }

        [Fact]
        public async Task GetMultiplePanelReadingsAsync_DelegatesToRepository()
        {
            var panelIds = new[] { 1, 2, 3 };
            var expected = new Dictionary<int, List<PanelReading>>
            {
                [1] = new List<PanelReading> { new() { PanelId = 1 } },
                [2] = new List<PanelReading>()
            };
            _repository.Setup(r => r.GetPanelReadingsByPanelIds(panelIds, null, null)).ReturnsAsync(expected);
            var service = CreateService();

            var result = await service.GetMultiplePanelReadingsAsync(panelIds);

            Assert.Same(expected, result);
        }

        [Fact]
        public async Task GetFlowConsumptionsOfPanelsAsync_DelegatesToRepository()
        {
            var panelIds = new[] { 1, 2 };
            var expected = new Dictionary<int, List<FlowConsumption>> { [1] = new List<FlowConsumption>() };
            _repository.Setup(r => r.GetFlowConsumptionsOfPanels(panelIds)).ReturnsAsync(expected);
            var service = CreateService();

            var result = await service.GetFlowConsumptionsOfPanelsAsync(panelIds);

            Assert.Same(expected, result);
        }
    }
}
