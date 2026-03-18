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
        return fetch(ChiggiStatsPage.buildUrl(path, params), {
            headers: {
                Authorization: 'MediaBrowser Token="' + ApiClient.accessToken() + '"'
            }
        }).then(response => {
            if (!response.ok) {
                throw new Error('HTTP ' + response.status);
            }

            return response.json();
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
                option.value = user.userId;
                option.textContent = user.userName;
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

        return Promise.all([overviewRequest, summaryRequest]).then(results => {
            const overview = results[0];
            const summary = results[1];

            if (overview && overview.metrics) {
                ChiggiStatsPage.renderMetrics(view, overview.metrics);
            } else {
                ChiggiStatsPage.renderMetrics(view, [
                    { key: 'hours', label: 'Hours Watched', value: String(summary.totalWatchTimeHours) },
                    { key: 'sessions', label: 'Sessions', value: String(summary.totalSessions) },
                    { key: 'movies', label: 'Movies', value: String(summary.movieCount) },
                    { key: 'episodes', label: 'Episodes', value: String(summary.episodeCount) }
                ]);
            }

            ChiggiStatsPage.renderTrend(view, summary.watchTimeByDay || []);
            ChiggiStatsPage.renderTopItems(view, summary.topItems || []);
        }).catch(error => {
            console.error('Chiggi Stats overview error', error);
            ChiggiStatsPage.renderEmpty(view.querySelector('#csOverviewMetrics'), 'Failed to load overview data.');
            ChiggiStatsPage.renderEmpty(view.querySelector('#csTrendBars'), 'Failed to load playback trend.');
            ChiggiStatsPage.renderTableEmpty(view.querySelector('#csTopItemsBody'), 4, 'Failed to load most watched items.');
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
            ChiggiStatsPage.state.playbackTotal = result.totalCount;
            ChiggiStatsPage.renderPlayback(view, result.items || []);
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
            ChiggiStatsPage.state.reportTotal = report.totalCount;
            view.querySelector('#csReportHeading').textContent = report.title;
            view.querySelector('#csReportSummary').textContent = report.totalCount + ' rows';
            ChiggiStatsPage.renderReport(view, report.columns || [], report.rows || []);
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
                '<div class="cs-card-value">' + ChiggiStatsPage.escape(metric.value) + '</div>' +
                '<span class="cs-card-label">' + ChiggiStatsPage.escape(metric.label) + '</span>';
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

        const maxMinutes = Math.max.apply(null, days.map(day => day.minutes));
        days.forEach(day => {
            const wrap = document.createElement('div');
            wrap.className = 'cs-trendBarWrap';

            const value = document.createElement('div');
            value.className = 'cs-trendValue';
            value.textContent = day.minutes + 'm';

            const bar = document.createElement('div');
            bar.className = 'cs-trendBar';
            bar.style.height = Math.max(8, Math.round((day.minutes / maxMinutes) * 150)) + 'px';

            const label = document.createElement('div');
            label.className = 'cs-trendLabel';
            label.textContent = day.date.slice(5);

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
            const title = item.seriesName || item.itemName;
            row.innerHTML =
                '<td>' + ChiggiStatsPage.escape(title) + '</td>' +
                '<td>' + ChiggiStatsPage.escape(item.mediaType) + '</td>' +
                '<td>' + ChiggiStatsPage.escape(String(item.watchCount)) + '</td>' +
                '<td>' + ChiggiStatsPage.escape(ChiggiStatsPage.formatMinutes(item.totalMinutes)) + '</td>';
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
            const title = item.seriesName
                ? item.seriesName + ' S' + (item.seasonNumber || '?') + 'E' + (item.episodeNumber || '?') + ' - ' + item.itemName
                : item.itemName;

            row.innerHTML =
                '<td>' + ChiggiStatsPage.escape(item.userName || '-') + '</td>' +
                '<td>' + ChiggiStatsPage.escape(title) + '</td>' +
                '<td>' + ChiggiStatsPage.escape(item.mediaType) + '</td>' +
                '<td>' + ChiggiStatsPage.escape(ChiggiStatsPage.formatMinutes(Math.round(item.durationMinutes))) + '</td>' +
                '<td>' + ChiggiStatsPage.escape(ChiggiStatsPage.formatDate(item.startTime)) + '</td>' +
                '<td>' + ChiggiStatsPage.escape(item.clientName || '-') + '</td>';
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
            th.textContent = column.label;
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
                td.textContent = row.cells[column.key] || '-';
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
