const ChiggiStatsPage = {
    pageSizePlayback: 50,
    pageSizeReports: 100,
    state: {
        activeTab: 'overview',
        playbackOffset: 0,
        playbackTotal: 0,
        reportOffset: 0,
        reportTotal: 0,
        reportType: 'movies',
        isAdmin: false
    },

    getPlaybackFilters: function (view) {
        return {
            userId: view.querySelector('#csUserSelect').value || undefined,
            mediaType: view.querySelector('#csMediaTypeSelect').value || undefined,
            startDate: view.querySelector('#csStartDate').value || undefined,
            endDate: view.querySelector('#csEndDate').value || undefined
        };
    },

    buildUrl: function (path, params) {
        const url = ApiClient.getUrl(path);
        const query = Object.entries(params || {})
            .filter(entry => entry[1] !== undefined && entry[1] !== null && entry[1] !== '')
            .map(entry => encodeURIComponent(entry[0]) + '=' + encodeURIComponent(entry[1]))
            .join('&');
        return query ? url + '?' + query : url;
    },

    fetchJson: function (path, params) {
        return ApiClient.ajax({
            type: 'GET',
            url: ChiggiStatsPage.buildUrl(path, params),
            dataType: 'json'
        });
    },

    setDefaultDates: function (view) {
        const now = new Date();
        const past = new Date(now);
        past.setDate(past.getDate() - 30);

        view.querySelector('#csStartDate').value = past.toISOString().slice(0, 10);
        view.querySelector('#csEndDate').value = now.toISOString().slice(0, 10);
    },

    loadUsers: function (view) {
        const userFilter = view.querySelector('#csUserFilter');
        const userSelect = view.querySelector('#csUserSelect');
        userSelect.innerHTML = '<option value="">All Users</option>';

        return ChiggiStatsPage.fetchJson('ChiggiStats/users').then(users => {
            ChiggiStatsPage.state.isAdmin = true;
            userFilter.classList.remove('cs-hidden');
            users.forEach(user => {
                const option = document.createElement('option');
                option.value = user.UserId;
                option.textContent = user.UserName;
                userSelect.appendChild(option);
            });
        }).catch(() => {
            ChiggiStatsPage.state.isAdmin = false;
            userFilter.classList.add('cs-hidden');
        }).finally(() => {
            ChiggiStatsPage.applyAdminVisibility(view);
        });
    },

    applyAdminVisibility: function (view) {
        view.querySelectorAll('[data-admin-only="true"]').forEach(element => {
            if (ChiggiStatsPage.state.isAdmin) {
                element.classList.remove('cs-hidden');
            } else {
                element.classList.add('cs-hidden');
            }
        });

        if (!ChiggiStatsPage.state.isAdmin && ChiggiStatsPage.isReportTab(ChiggiStatsPage.state.activeTab)) {
            ChiggiStatsPage.selectTab(view, 'overview');
        }
    },

    isReportTab: function (tab) {
        return ['movies', 'series', 'seasons', 'episodes', 'music', 'boxsets', 'users', 'devices'].includes(tab);
    },

    selectTab: function (view, tab) {
        if (ChiggiStatsPage.isReportTab(tab) && !ChiggiStatsPage.state.isAdmin) {
            tab = 'overview';
        }

        ChiggiStatsPage.state.activeTab = tab;
        view.querySelectorAll('[data-tab]').forEach(button => {
            if (button.dataset.tab === tab) {
                button.classList.add('cs-tabButton-active');
            } else {
                button.classList.remove('cs-tabButton-active');
            }
        });

        const showPlaybackFilters = tab === 'overview' || tab === 'playback';
        view.querySelector('#csPlaybackFilters').classList.toggle('cs-hidden', !showPlaybackFilters);
        view.querySelector('#csOverviewSection').classList.toggle('cs-hidden', tab !== 'overview');
        view.querySelector('#csPlaybackSection').classList.toggle('cs-hidden', tab !== 'playback');
        view.querySelector('#csReportSection').classList.toggle('cs-hidden', !ChiggiStatsPage.isReportTab(tab));

        if (tab === 'overview') {
            ChiggiStatsPage.state.playbackOffset = 0;
            ChiggiStatsPage.loadOverview(view);
        } else if (tab === 'playback') {
            ChiggiStatsPage.state.playbackOffset = 0;
            ChiggiStatsPage.loadPlayback(view);
        } else if (ChiggiStatsPage.isReportTab(tab)) {
            ChiggiStatsPage.state.reportType = tab;
            ChiggiStatsPage.state.reportOffset = 0;
            ChiggiStatsPage.loadReport(view);
        }
    },

    loadOverview: function (view) {
        Dashboard.showLoadingMsg();
        const summaryRequest = ChiggiStatsPage.fetchJson('ChiggiStats/summary', ChiggiStatsPage.getPlaybackFilters(view));
        const overviewRequest = ChiggiStatsPage.state.isAdmin
            ? ChiggiStatsPage.fetchJson('ChiggiStats/reports/overview')
            : Promise.resolve(null);

        return Promise.allSettled([overviewRequest, summaryRequest]).then(results => {
            const overviewResult = results[0];
            const summaryResult = results[1];

            if (summaryResult.status !== 'fulfilled') {
                console.error('Chiggi Stats summary error', summaryResult.reason);
                ChiggiStatsPage.renderEmpty(view.querySelector('#csOverviewMetrics'), 'Failed to load overview data.');
                ChiggiStatsPage.renderEmpty(view.querySelector('#csTrendBars'), 'Failed to load playback trend.');
                ChiggiStatsPage.renderTableEmpty(view.querySelector('#csTopItemsBody'), 4, 'Failed to load most watched items.');
                return;
            }

            const summary = summaryResult.value;
            const overview = overviewResult.status === 'fulfilled' ? overviewResult.value : null;

            if (overview && overview.Metrics) {
                ChiggiStatsPage.renderMetrics(view, overview.Metrics);
            } else {
                ChiggiStatsPage.renderMetrics(view, [
                    { Key: 'hours',    Label: 'Hours Watched', Value: String(summary.TotalWatchTimeHours) },
                    { Key: 'sessions', Label: 'Sessions',      Value: String(summary.TotalSessions) },
                    { Key: 'movies',   Label: 'Movies',        Value: String(summary.MovieCount) },
                    { Key: 'episodes', Label: 'Episodes',      Value: String(summary.EpisodeCount) }
                ]);
            }

            ChiggiStatsPage.renderTrend(view, summary.WatchTimeByDay || []);
            ChiggiStatsPage.renderTopItems(view, summary.TopItems || []);
        }).finally(() => {
            Dashboard.hideLoadingMsg();
        });
    },

    loadPlayback: function (view) {
        Dashboard.showLoadingMsg();
        const filters = ChiggiStatsPage.getPlaybackFilters(view);
        filters.limit = ChiggiStatsPage.pageSizePlayback;
        filters.offset = ChiggiStatsPage.state.playbackOffset;

        return ChiggiStatsPage.fetchJson('ChiggiStats/activity', filters).then(result => {
            ChiggiStatsPage.state.playbackTotal = result.TotalCount;
            ChiggiStatsPage.renderPlayback(view, result.Items || []);
            ChiggiStatsPage.updatePlaybackPagination(view);
        }).catch(error => {
            console.error('Chiggi Stats playback error', error);
            ChiggiStatsPage.renderTableEmpty(view.querySelector('#csActivityBody'), 6, 'Failed to load playback activity.');
        }).finally(() => {
            Dashboard.hideLoadingMsg();
        });
    },

    loadReport: function (view) {
        Dashboard.showLoadingMsg();
        const reportType = ChiggiStatsPage.state.reportType;
        const limit = ChiggiStatsPage.pageSizeReports;
        const offset = ChiggiStatsPage.state.reportOffset;

        return ChiggiStatsPage.fetchJson('ChiggiStats/reports/table', {
            type: reportType,
            limit: limit,
            offset: offset
        }).then(report => {
            ChiggiStatsPage.state.reportTotal = report.TotalCount;
            view.querySelector('#csReportHeading').textContent = report.Title;
            view.querySelector('#csReportSummary').textContent = report.TotalCount + ' rows';
            ChiggiStatsPage.renderReport(view, report.Columns || [], report.Rows || []);
            ChiggiStatsPage.updateReportPagination(view);
        }).catch(error => {
            console.error('Chiggi Stats report error', error);
            view.querySelector('#csReportHeading').textContent = 'Report';
            view.querySelector('#csReportSummary').textContent = 'Failed to load report.';
            view.querySelector('#csReportHead').innerHTML = '';
            ChiggiStatsPage.renderTableEmpty(view.querySelector('#csReportBody'), 1, 'Failed to load report data.');
        }).finally(() => {
            Dashboard.hideLoadingMsg();
        });
    },

    renderMetrics: function (view, metrics) {
        const container = view.querySelector('#csOverviewMetrics');
        container.innerHTML = '';

        if (!metrics.length) {
            ChiggiStatsPage.renderEmpty(container, 'No overview metrics available.');
            return;
        }

        metrics.forEach(metric => {
            const card = document.createElement('div');
            card.className = 'cs-card';
            card.innerHTML =
                '<div class="cs-card-value">' + ChiggiStatsPage.escape(metric.Value) + '</div>' +
                '<span class="cs-card-label">' + ChiggiStatsPage.escape(metric.Label) + '</span>';
            container.appendChild(card);
        });
    },

    renderTrend: function (view, days) {
        const container = view.querySelector('#csTrendBars');
        container.innerHTML = '';

        if (!days.length) {
            ChiggiStatsPage.renderEmpty(container, 'No playback trend available for the selected period.');
            return;
        }

        const maxMinutes = Math.max.apply(null, days.map(day => day.Minutes));
        days.forEach(day => {
            const wrap = document.createElement('div');
            wrap.className = 'cs-trendBarWrap';

            const value = document.createElement('div');
            value.className = 'cs-trendValue';
            value.textContent = day.Minutes + 'm';

            const bar = document.createElement('div');
            bar.className = 'cs-trendBar';
            bar.style.height = Math.max(8, Math.round((day.Minutes / maxMinutes) * 150)) + 'px';

            const label = document.createElement('div');
            label.className = 'cs-trendLabel';
            label.textContent = day.Date.slice(5);

            wrap.appendChild(value);
            wrap.appendChild(bar);
            wrap.appendChild(label);
            container.appendChild(wrap);
        });
    },

    renderTopItems: function (view, items) {
        const body = view.querySelector('#csTopItemsBody');
        if (!items.length) {
            ChiggiStatsPage.renderTableEmpty(body, 4, 'No playback history found for the selected filters.');
            return;
        }

        body.innerHTML = '';
        items.forEach(item => {
            const row = document.createElement('tr');
            const title = item.SeriesName || item.ItemName;
            row.innerHTML =
                '<td>' + ChiggiStatsPage.escape(title) + '</td>' +
                '<td>' + ChiggiStatsPage.escape(item.MediaType) + '</td>' +
                '<td>' + ChiggiStatsPage.escape(String(item.WatchCount)) + '</td>' +
                '<td>' + ChiggiStatsPage.escape(ChiggiStatsPage.formatMinutes(item.TotalMinutes)) + '</td>';
            body.appendChild(row);
        });
    },

    renderPlayback: function (view, items) {
        const body = view.querySelector('#csActivityBody');
        if (!items.length) {
            ChiggiStatsPage.renderTableEmpty(body, 6, 'No playback history found for the selected filters.');
            return;
        }

        body.innerHTML = '';
        items.forEach(item => {
            const row = document.createElement('tr');
            const title = item.SeriesName
                ? item.SeriesName + ' S' + (item.SeasonNumber || '?') + 'E' + (item.EpisodeNumber || '?') + ' - ' + item.ItemName
                : item.ItemName;

            row.innerHTML =
                '<td>' + ChiggiStatsPage.escape(item.UserName || '-') + '</td>' +
                '<td>' + ChiggiStatsPage.escape(title) + '</td>' +
                '<td>' + ChiggiStatsPage.escape(item.MediaType) + '</td>' +
                '<td>' + ChiggiStatsPage.escape(ChiggiStatsPage.formatMinutes(Math.round(item.DurationMinutes))) + '</td>' +
                '<td>' + ChiggiStatsPage.escape(ChiggiStatsPage.formatDate(item.StartTime)) + '</td>' +
                '<td>' + ChiggiStatsPage.escape(item.ClientName || '-') + '</td>';
            body.appendChild(row);
        });
    },

    renderReport: function (view, columns, rows) {
        const head = view.querySelector('#csReportHead');
        const body = view.querySelector('#csReportBody');
        head.innerHTML = '';
        body.innerHTML = '';

        if (!columns.length) {
            ChiggiStatsPage.renderTableEmpty(body, 1, 'No columns available for this report.');
            return;
        }

        const headRow = document.createElement('tr');
        columns.forEach(column => {
            const th = document.createElement('th');
            th.textContent = column.Label;
            headRow.appendChild(th);
        });
        head.appendChild(headRow);

        if (!rows.length) {
            ChiggiStatsPage.renderTableEmpty(body, columns.length, 'No rows available for this report.');
            return;
        }

        rows.forEach(row => {
            const tr = document.createElement('tr');
            columns.forEach(column => {
                const td = document.createElement('td');
                td.textContent = row.Cells[column.Key] || '-';
                tr.appendChild(td);
            });
            body.appendChild(tr);
        });
    },

    updatePlaybackPagination: function (view) {
        const totalPages = Math.max(1, Math.ceil(ChiggiStatsPage.state.playbackTotal / ChiggiStatsPage.pageSizePlayback));
        const currentPage = Math.floor(ChiggiStatsPage.state.playbackOffset / ChiggiStatsPage.pageSizePlayback) + 1;
        view.querySelector('#csPlaybackPageInfo').textContent = 'Page ' + currentPage + ' of ' + totalPages;
        view.querySelector('#csPlaybackPrev').disabled = ChiggiStatsPage.state.playbackOffset === 0;
        view.querySelector('#csPlaybackNext').disabled = ChiggiStatsPage.state.playbackOffset + ChiggiStatsPage.pageSizePlayback >= ChiggiStatsPage.state.playbackTotal;
    },

    updateReportPagination: function (view) {
        const totalPages = Math.max(1, Math.ceil(ChiggiStatsPage.state.reportTotal / ChiggiStatsPage.pageSizeReports));
        const currentPage = Math.floor(ChiggiStatsPage.state.reportOffset / ChiggiStatsPage.pageSizeReports) + 1;
        view.querySelector('#csReportPageInfo').textContent = 'Page ' + currentPage + ' of ' + totalPages;
        view.querySelector('#csReportPrev').disabled = ChiggiStatsPage.state.reportOffset === 0;
        view.querySelector('#csReportNext').disabled = ChiggiStatsPage.state.reportOffset + ChiggiStatsPage.pageSizeReports >= ChiggiStatsPage.state.reportTotal;
    },

    renderEmpty: function (container, message) {
        container.innerHTML = '<div class="cs-empty">' + ChiggiStatsPage.escape(message) + '</div>';
    },

    renderTableEmpty: function (tbody, colspan, message) {
        tbody.innerHTML = '<tr><td colspan="' + colspan + '" class="cs-empty">' + ChiggiStatsPage.escape(message) + '</td></tr>';
    },

    formatMinutes: function (minutes) {
        if (minutes < 60) {
            return minutes + 'm';
        }

        const hours = Math.floor(minutes / 60);
        const mins = minutes % 60;
        return mins > 0 ? hours + 'h ' + mins + 'm' : hours + 'h';
    },

    formatDate: function (isoValue) {
        if (!isoValue) {
            return '-';
        }

        const value = new Date(isoValue);
        return value.toLocaleDateString() + ' ' + value.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
    },

    escape: function (value) {
        return String(value)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#39;');
    }
};

export default function (view) {
    view.querySelectorAll('[data-tab]').forEach(button => {
        button.addEventListener('click', function () {
            ChiggiStatsPage.selectTab(view, this.dataset.tab);
        });
    });

    view.querySelector('#csApplyFilters').addEventListener('click', function () {
        if (ChiggiStatsPage.state.activeTab === 'playback') {
            ChiggiStatsPage.state.playbackOffset = 0;
            ChiggiStatsPage.loadPlayback(view);
        } else {
            ChiggiStatsPage.loadOverview(view);
        }
    });

    view.querySelector('#csResetFilters').addEventListener('click', function () {
        view.querySelector('#csUserSelect').value = '';
        view.querySelector('#csMediaTypeSelect').value = '';
        ChiggiStatsPage.setDefaultDates(view);

        if (ChiggiStatsPage.state.activeTab === 'playback') {
            ChiggiStatsPage.state.playbackOffset = 0;
            ChiggiStatsPage.loadPlayback(view);
        } else {
            ChiggiStatsPage.loadOverview(view);
        }
    });

    view.querySelector('#csPlaybackPrev').addEventListener('click', function () {
        if (ChiggiStatsPage.state.playbackOffset >= ChiggiStatsPage.pageSizePlayback) {
            ChiggiStatsPage.state.playbackOffset -= ChiggiStatsPage.pageSizePlayback;
            ChiggiStatsPage.loadPlayback(view);
        }
    });

    view.querySelector('#csPlaybackNext').addEventListener('click', function () {
        if (ChiggiStatsPage.state.playbackOffset + ChiggiStatsPage.pageSizePlayback < ChiggiStatsPage.state.playbackTotal) {
            ChiggiStatsPage.state.playbackOffset += ChiggiStatsPage.pageSizePlayback;
            ChiggiStatsPage.loadPlayback(view);
        }
    });

    view.querySelector('#csReportPrev').addEventListener('click', function () {
        if (ChiggiStatsPage.state.reportOffset >= ChiggiStatsPage.pageSizeReports) {
            ChiggiStatsPage.state.reportOffset -= ChiggiStatsPage.pageSizeReports;
            ChiggiStatsPage.loadReport(view);
        }
    });

    view.querySelector('#csReportNext').addEventListener('click', function () {
        if (ChiggiStatsPage.state.reportOffset + ChiggiStatsPage.pageSizeReports < ChiggiStatsPage.state.reportTotal) {
            ChiggiStatsPage.state.reportOffset += ChiggiStatsPage.pageSizeReports;
            ChiggiStatsPage.loadReport(view);
        }
    });

    view.addEventListener('viewshow', function () {
        ChiggiStatsPage.setDefaultDates(view);
        ChiggiStatsPage.loadUsers(view).finally(function () {
            ChiggiStatsPage.selectTab(view, ChiggiStatsPage.state.activeTab);
        });
    });
}
