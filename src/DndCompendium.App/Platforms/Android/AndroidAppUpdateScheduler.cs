using Android.App;
using Android.App.Job;
using Android.Content;
using Android.OS;
using DndCompendium.App.Services;
using DndCompendium.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;

namespace DndCompendium.App;

public sealed class AndroidAppUpdateScheduler : IAppUpdateScheduler
{
    private const int JobId = 0x444E44;
    public void Configure(bool enabled)
    {
        try
        {
            var context = Android.App.Application.Context;
            var scheduler = (JobScheduler?)context.GetSystemService(Context.JobSchedulerService);
            if (scheduler is null) return;
            if (!enabled)
            {
                scheduler.Cancel(JobId);
                return;
            }
            using var component = new ComponentName(context, Java.Lang.Class.FromType(typeof(AppUpdateJobService)));
            using var builder = new JobInfo.Builder(JobId, component);
            using var job = builder
                .SetRequiredNetworkType(NetworkType.Any)!
                .SetPeriodic(24 * 60 * 60 * 1000L)!
                .SetPersisted(false)!
                .Build();
            if (job is not null) scheduler.Schedule(job);
        }
        catch
        {
            // A background check must never prevent the offline application from opening.
        }
    }
}

[Service(
    Name = "com.march.dndcompendium.AppUpdateJobService",
    Permission = "android.permission.BIND_JOB_SERVICE",
    Exported = false)]
public sealed class AppUpdateJobService : JobService
{
    public override bool OnStartJob(JobParameters? parameters)
    {
        _ = RunAsync(parameters);
        return true;
    }

    private async Task RunAsync(JobParameters? parameters)
    {
        try
        {
            var coordinator = IPlatformApplication.Current?.Services.GetService<AppUpdateCoordinator>();
            if (coordinator is not null) await coordinator.CheckAsync(manual: false);
        }
        catch { }
        finally
        {
            if (parameters is not null) JobFinished(parameters, false);
        }
    }

    public override bool OnStopJob(JobParameters? parameters) => true;
}
