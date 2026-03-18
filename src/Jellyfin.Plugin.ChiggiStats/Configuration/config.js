const ChiggiStatsConfigPage = {
    pluginId: 'a8e82834-4b5e-4b16-a7c4-6ea5d3a4e312',

    loadConfiguration: function (view) {
        Dashboard.showLoadingMsg();
        return ApiClient.getPluginConfiguration(ChiggiStatsConfigPage.pluginId).then(config => {
            view.querySelector('#EnableSqliteTracking').checked = config.EnableSqliteTracking !== false;
            view.querySelector('#DataRetentionDays').value = config.DataRetentionDays || 365;
            view.querySelector('#MinimumPlaybackSeconds').value = config.MinimumPlaybackSeconds || 30;
        }).finally(() => {
            Dashboard.hideLoadingMsg();
        });
    },

    saveConfiguration: function (view) {
        Dashboard.showLoadingMsg();
        return ApiClient.getPluginConfiguration(ChiggiStatsConfigPage.pluginId).then(config => {
            config.EnableSqliteTracking = view.querySelector('#EnableSqliteTracking').checked;
            config.DataRetentionDays = parseInt(view.querySelector('#DataRetentionDays').value, 10) || 365;
            config.MinimumPlaybackSeconds = parseInt(view.querySelector('#MinimumPlaybackSeconds').value, 10) || 30;
            return ApiClient.updatePluginConfiguration(ChiggiStatsConfigPage.pluginId, config);
        }).then(result => {
            Dashboard.processPluginConfigurationUpdateResult(result);
        }).finally(() => {
            Dashboard.hideLoadingMsg();
        });
    }
};

export default function (view) {
    view.querySelector('#chiggiStatsConfigForm').addEventListener('submit', function (event) {
        event.preventDefault();
        ChiggiStatsConfigPage.saveConfiguration(view);
        return false;
    });

    view.addEventListener('viewshow', function () {
        ChiggiStatsConfigPage.loadConfiguration(view);
    });
}
