package com.nexonic.downloadmanager.service

import android.app.PendingIntent
import android.appwidget.AppWidgetManager
import android.appwidget.AppWidgetProvider
import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.widget.RemoteViews
import com.nexonic.downloadmanager.MainActivity
import com.nexonic.downloadmanager.R
import com.nexonic.downloadmanager.core.Format
import com.nexonic.downloadmanager.core.tr
import com.nexonic.downloadmanager.data.Downloads
import com.nexonic.downloadmanager.data.Status

/** Home-screen widget: what is downloading and how fast, Pause all, and Add the copied link (opens the app's Add window). */
class NexonicWidget : AppWidgetProvider() {
    override fun onUpdate(context: Context, manager: AppWidgetManager, ids: IntArray) = refresh(context)

    override fun onReceive(context: Context, intent: Intent) {
        if (intent.action == ACTION_PAUSE_ALL) {
            Downloads.init(context)
            Downloads.pauseAll()
            refresh(context)
            return
        }
        super.onReceive(context, intent)
    }

    companion object {
        const val ACTION_PAUSE_ALL = "com.nexonic.downloadmanager.widget.PAUSE_ALL"
        const val ACTION_ADD_COPIED = "com.nexonic.downloadmanager.ADD_COPIED"
        private var last = 0L

        /** Redraws every placed widget; at most once a second while downloads run. */
        fun refresh(context: Context, force: Boolean = true) {
            val now = System.currentTimeMillis()
            if (!force && now - last < 1000) return
            last = now
            val app = context.applicationContext
            val manager = AppWidgetManager.getInstance(app)
            val ids = runCatching { manager.getAppWidgetIds(ComponentName(app, NexonicWidget::class.java)) }.getOrNull() ?: return
            if (ids.isEmpty()) return
            Downloads.init(app)
            val active = Downloads.items.value.count { it.status == Status.DOWNLOADING }
            val waiting = Downloads.items.value.count { it.status == Status.QUEUED }
            val views = RemoteViews(app.packageName, R.layout.nexonic_widget).apply {
                setTextViewText(R.id.widget_title, when {
                    active > 0 -> tr("Downloading %d").format(active)
                    waiting > 0 -> tr("%d waiting").format(waiting)
                    else -> tr("No active downloads")
                })
                setTextViewText(R.id.widget_speed, if (active > 0) Format.speed(Downloads.totalSpeed) else "Nexonic")
                val flags = PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
                setOnClickPendingIntent(R.id.widget_pause, PendingIntent.getBroadcast(app, 10,
                    Intent(app, NexonicWidget::class.java).setAction(ACTION_PAUSE_ALL), flags))
                setOnClickPendingIntent(R.id.widget_add, PendingIntent.getActivity(app, 11,
                    Intent(app, MainActivity::class.java).setAction(ACTION_ADD_COPIED).addFlags(Intent.FLAG_ACTIVITY_SINGLE_TOP), flags))
                setOnClickPendingIntent(R.id.widget_root, PendingIntent.getActivity(app, 12,
                    Intent(app, MainActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_SINGLE_TOP), flags))
            }
            manager.updateAppWidget(ids, views)
        }
    }
}
