define(['baseView', 'loading', 'emby-input', 'emby-button', 'emby-checkbox', 'emby-scroller', 'emby-select'], function (BaseView, loading) {
    'use strict';

    function onSubmit(e) {

        e.preventDefault();

        var instance = this;
        var form = this.view;

        loading.show();

        ApiClient.getPluginConfiguration("1d0dddf7-1877-4473-8d7b-03f7dac1e559").then(function (config) {

            config.TidyGenreList = form.querySelector('.chkTidyGenres').checked;
            config.AniDB_wait_time = form.querySelector('.chkAniDB_wait_time').value;

            ApiClient.updatePluginConfiguration("1d0dddf7-1877-4473-8d7b-03f7dac1e559", config).then(Dashboard.processPluginConfigurationUpdateResult);
        });

        // Disable default form submission
        return false;
    }

    function View(view, params) {
        BaseView.apply(this, arguments);

        view.querySelector('form').addEventListener('submit', onSubmit.bind(this));
    }

    Object.assign(View.prototype, BaseView.prototype);

    View.prototype.onResume = function (options) {

        BaseView.prototype.onResume.apply(this, arguments);

        var instance = this;

        loading.show();

        ApiClient.getPluginConfiguration("1d0dddf7-1877-4473-8d7b-03f7dac1e559").then(function (config) {

            var view = instance.view;

            view.querySelector('.chkTidyGenres').checked = config.TidyGenreList;
            view.querySelector('.chkAniDB_wait_time').value = config.AniDB_wait_time;

            loading.hide();
        });
    };

    return View;
});
