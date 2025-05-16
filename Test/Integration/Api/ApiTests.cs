using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Quartz;
using Quartz.Impl;
using StocksReportingLibrary.Application.Report;
using StocksReportingLibrary.Application.Services.Scheduling;
using StocksReportingLibrary.Configuration;
using StocksReportingLibrary.Presentation.Email;
using StocksReportingLibrary.Presentation.Report;
using Xunit;

namespace Test.Integration.Api;
public class ApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public ApiTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task Should_CreateEmail_WithValidData()
    {
        var email = Guid.NewGuid() + "@example.com";
        await (
            (await new When(_client).WithCreateEmailRequest(email).Then()).ShouldBeSuccessful()
        ).AndShouldReturn<CreateEmailEndpoint.Response>(result =>
        {
            result.Should().NotBeNull();
            result.Email.EmailValue.Should().Be(email);
        });
    }

    [Fact]
    public async Task Should_ListEmails_ReturnEmails()
    {
        await (
            (await new When(_client).WithListEmailRequest(1, 1).Then()).ShouldBeSuccessful()
        ).AndShouldReturn<ListEmailsEndpoint.Response>(result =>
        {
            result.Should().NotBeNull();
            result.Emails.Should().NotBeNull();
        });
    }

    [Fact]
    public async Task Should_DeleteEmail_WithValidId()
    {
        var email = "delete@example.com";
        var createdEmail = await (
            (await new When(_client).WithCreateEmailRequest(email).Then()).ShouldBeSuccessful()
        ).AndShouldReturn<CreateEmailEndpoint.Response>(result =>
        {
            result.Should().NotBeNull();
            return result;
        });

        (
            await new When(_client).WithEmailId(createdEmail.Email.Id).Then()
        ).ShouldBeSuccessful();
    }

    [Fact]
    public async Task Should_GetReport_WithValidId()
    {
        var reports = await (
            await new When(_client).WithCreateReportJob().WithListReportsRequest(1, 1).Then()
        )
            .ShouldBeSuccessful()
            .AndShouldReturn<ListReportsEndpoint.Response>(result =>
            {
                result.Should().NotBeNull();
                result.Reports.Should().NotBeEmpty();
                return result;
            });

        await (
            (
                await new When(_client).WithReportId(reports.Reports.First().Id).Then()
            ).ShouldBeSuccessful()
        ).AndShouldReturn<GetReportEndpoint.Response>(result =>
        {
            result.Should().NotBeNull();
            result.Id.Should().Be(reports.Reports.First().Id);
        });
    }

    [Fact]
    public async Task Should_ListReports_ReturnReports()
    {
        await (
            await new When(_client).WithCreateReportJob().WithListReportsRequest(1, 10).Then()
        )
            .ShouldBeSuccessful()
            .AndShouldReturn<ListReportsEndpoint.Response>(result =>
            {
                result.Should().NotBeNull();
                result.Reports.Should().NotBeNull();
            });
    }

    [Fact]
    public async Task Should_SendReport_WithValidData()
    {
        var reports = await (
            await new When(_client).WithCreateReportJob().WithListReportsRequest(1, 1).Then()
        )
            .ShouldBeSuccessful()
            .AndShouldReturn<ListReportsEndpoint.Response>(result =>
            {
                result.Should().NotBeNull();
                result.Reports.Should().NotBeEmpty();
                return result;
            });

        var email = Guid.NewGuid() + "@example.com";
        var createdEmail = await (
            (await new When(_client).WithCreateEmailRequest(email).Then()).ShouldBeSuccessful()
        ).AndShouldReturn<CreateEmailEndpoint.Response>(result =>
        {
            result.Should().NotBeNull();
            result.Email.EmailValue.Should().Be(email);
            return result;
        });

        await (
            (
                await (
                    new When(_client).WithSendReportRequest(
                        reports.Reports.First().Id,
                        [createdEmail.Email.Id]
                    )
                ).Then()
            ).ShouldBeSuccessful()
        ).AndShouldReturn<SendReportEndpoint.Response>(result =>
        {
            result.Should().NotBeNull();
            result.Report.ReportId.Should().Be(reports.Reports.First().Id);
        });
    }

    [Fact]
    public async Task Should_CreateMultipleEmails_AndListEmails_ReturnsAll()
    {
        var emails = new List<string>
        {
            Guid.NewGuid() + "@example.com",
            Guid.NewGuid() + "@example.com",
            Guid.NewGuid() + "@example.com",
        };

        foreach (var email in emails)
        {
            (await new When(_client).WithCreateEmailRequest(email).Then()).ShouldBeSuccessful();
        }

        await (
            (await new When(_client).WithListEmailRequest(1, 10).Then()).ShouldBeSuccessful()
        ).AndShouldReturn<ListEmailsEndpoint.Response>(result =>
        {
            result.Should().NotBeNull();
            emails.All(e => result.Emails.Any(x => x.EmailValue == e)).Should().BeTrue();
        });
    }

    [Fact]
    public async Task Should_SendReport_ToMultipleEmails()
    {
        var reports = await (
            await new When(_client).WithCreateReportJob().WithListReportsRequest(1, 1).Then()
        )
            .ShouldBeSuccessful()
            .AndShouldReturn<ListReportsEndpoint.Response>(result =>
            {
                result.Should().NotBeNull();
                result.Reports.Should().NotBeEmpty();
                return result;
            });

        var email1 = Guid.NewGuid() + "@example.com";
        var email2 = Guid.NewGuid() + "@example.com";

        var createdEmail1 = await (
            (await new When(_client).WithCreateEmailRequest(email1).Then()).ShouldBeSuccessful()
        ).AndShouldReturn<CreateEmailEndpoint.Response>(result => result);

        var createdEmail2 = await (
            (await new When(_client).WithCreateEmailRequest(email2).Then()).ShouldBeSuccessful()
        ).AndShouldReturn<CreateEmailEndpoint.Response>(result => result);

        await (
            (
                await (
                    new When(_client).WithSendReportRequest(
                        reports.Reports.First().Id,
                        [createdEmail1.Email.Id, createdEmail2.Email.Id]
                    )
                ).Then()
            ).ShouldBeSuccessful()
        ).AndShouldReturn<SendReportEndpoint.Response>(result =>
        {
            result.Should().NotBeNull();
            result.Report.ReportId.Should().Be(reports.Reports.First().Id);
        });
    }

    [Fact]
    public async Task Should_DeleteEmail_DoesNotAffectOtherEmails()
    {
        var email1 = Guid.NewGuid() + "@example.com";
        var email2 = Guid.NewGuid() + "@example.com";

        var createdEmail1 = await (
            (await new When(_client).WithCreateEmailRequest(email1).Then()).ShouldBeSuccessful()
        ).AndShouldReturn<CreateEmailEndpoint.Response>(result => result);

        var createdEmail2 = await (
            (await new When(_client).WithCreateEmailRequest(email2).Then()).ShouldBeSuccessful()
        ).AndShouldReturn<CreateEmailEndpoint.Response>(result => result);

        (
            await new When(_client).WithEmailId(createdEmail1.Email.Id).Then()
        ).ShouldBeSuccessful();

        await (
            (await new When(_client).WithListEmailRequest(1, 10).Then()).ShouldBeSuccessful()
        ).AndShouldReturn<ListEmailsEndpoint.Response>(result =>
        {
            result.Emails.Any(e => e.Id == createdEmail2.Email.Id).Should().BeTrue();
        });
    }

    [Fact]
    public async Task Should_NotCreateEmail_WithInvalidEmail_ShouldFail()
    {
        (
            await new When(_client).WithCreateEmailRequest("invalid-email").Then()
        ).ShouldBeFailure();
    }

    [Fact]
    public async Task Should_GetReport_WithInvalidId_ShouldFail()
    {
        (await new When(_client).WithReportId(Guid.NewGuid()).Then()).ShouldBeFailure();
    }

    private sealed class When : IAsyncEnumerable<When>
    {
        private readonly HttpClient _client;
        private CreateEmailEndpoint.Request? _createEmailRequest;
        private Guid? _emailId;
        private Guid? _reportId;
        private SendReportEndpoint.Request? _sendReportRequest;
        private ListReportsRequest? _listReportsRequest;
        private ListEmailsRequest? _listEmailsRequest;
        private HttpResponseMessage? _response;
        private bool _createReportJob = false;

        public When(HttpClient client)
        {
            _client = client;
        }

        public When WithCreateEmailRequest(string email)
        {
            _createEmailRequest = new CreateEmailEndpoint.Request(email);
            return this;
        }

        public When WithListEmailRequest(int page, int pageSize)
        {
            _listEmailsRequest = new ListEmailsRequest(page, pageSize);
            return this;
        }

        public When WithEmailId(Guid id)
        {
            _emailId = id;
            return this;
        }

        public When WithReportId(Guid id)
        {
            _reportId = id;
            return this;
        }

        public When WithSendReportRequest(Guid reportId, List<Guid> emailIds)
        {
            _sendReportRequest = new SendReportEndpoint.Request(reportId, emailIds);
            return this;
        }

        public When WithListReportsRequest(int page, int pageSize)
        {
            _listReportsRequest = new ListReportsRequest(page, pageSize);
            return this;
        }

        public When WithCreateReportJob()
        {
            _createReportJob = true;
            return this;
        }

        public async Task<When> Then()
        {
            if (_createReportJob)
            {
                await TriggerReportJob();
            }

            if (_createEmailRequest != null)
            {
                _response = await _client.PostAsJsonAsync("emails", _createEmailRequest);
            }
            else if (_emailId.HasValue)
            {
                _response = await _client.DeleteAsync($"emails/{_emailId.Value}");
            }
            else if (_reportId.HasValue)
            {
                _response = await _client.GetAsync($"reports/{_reportId.Value}");
            }
            else if (_sendReportRequest != null)
            {
                _response = await _client.PostAsJsonAsync("reports/send", _sendReportRequest);
            }
            else if (_listReportsRequest != null)
            {
                _response = await _client.GetAsync(
                    $"reports?page={_listReportsRequest.Page}&pageSize={_listReportsRequest.PageSize}"
                );
            }
            else if (_listEmailsRequest != null)
            {
                _response = await _client.GetAsync(
                    $"emails?page={_listEmailsRequest.Page}&pageSize={_listEmailsRequest.PageSize}"
                );
            }
            else
            {
                throw new InvalidOperationException("No request specified.");
            }
            return this;
        }

        public When ShouldBeSuccessful()
        {
            _response!.EnsureSuccessStatusCode();
            return this;
        }

        public async Task<When> AndShouldReturn<T>(Action<T> assert)
        {
            var result = await _response!.Content.ReadFromJsonAsync<T>();
            result.Should().NotBeNull();
            assert(result!);
            return this;
        }

        public async Task<T> AndShouldReturn<T>(Func<T, T> assert)
        {
            var result = await _response!.Content.ReadFromJsonAsync<T>();
            result.Should().NotBeNull();
            return assert(result!);
        }

        private async Task TriggerReportJob()
        {
            ISchedulerFactory schedulerFactory = new StdSchedulerFactory();
            IScheduler scheduler = await schedulerFactory.GetScheduler();
            await scheduler.Start();

            var jobDetail = JobBuilder
                .Create<CreateReportJob>()
                .WithIdentity("CreateReportJob")
                .Build();

            var trigger = TriggerBuilder
                .Create()
                .WithIdentity("CreateReportJobTrigger")
                .StartNow()
                .Build();

            await scheduler.ScheduleJob(jobDetail, trigger);

            await Task.Delay(3000);

            await scheduler.Shutdown();
        }

        public When ShouldBeFailure()
        {
            _response!.IsSuccessStatusCode.Should().BeFalse();
            return this;
        }

        public IAsyncEnumerator<When> GetAsyncEnumerator(
            CancellationToken cancellationToken = default
        )
        {
            return new AsyncEnumerator(this);
        }

        private class AsyncEnumerator : IAsyncEnumerator<When>
        {
            private readonly When _when;
            private bool _moved = false;

            public AsyncEnumerator(When when)
            {
                _when = when;
            }

            public When Current => _when;

            public ValueTask<bool> MoveNextAsync()
            {
                if (!_moved)
                {
                    _moved = true;
                    return new ValueTask<bool>(true);
                }
                return new ValueTask<bool>(false);
            }

            public ValueTask DisposeAsync()
            {
                _when._response?.Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }

    private record ListReportsRequest(int Page, int PageSize);

    private record ListEmailsRequest(int Page, int PageSize);
}
