plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
    id("org.jetbrains.kotlin.plugin.compose")
}

android {
    namespace = "com.nexonic.downloadmanager"
    compileSdk = 35

    defaultConfig {
        applicationId = "com.nexonic.downloadmanager"
        minSdk = 29
        targetSdk = 35
        // Same version as the Mac and Windows apps (VERSION at the repository root), e.g. 0.6.7 -> code 607.
        val version = rootProject.file("../VERSION").readText().trim()
        val (major, minor, patch) = version.split(".").map { it.takeWhile(Char::isDigit).ifEmpty { "0" }.toInt() }
        versionCode = major * 10000 + minor * 100 + patch
        versionName = version
    }

    // Release signing comes from the environment (GitHub secrets); keys are never stored in the repository.
    val keystore = System.getenv("NDM_ANDROID_KEYSTORE")
    signingConfigs {
        if (!keystore.isNullOrEmpty() && file(keystore).exists()) {
            create("release") {
                storeFile = file(keystore)
                storeType = "pkcs12"
                storePassword = System.getenv("NDM_ANDROID_KEYSTORE_PASSWORD")
                keyAlias = System.getenv("NDM_ANDROID_KEY_ALIAS")
                keyPassword = System.getenv("NDM_ANDROID_KEY_PASSWORD")
            }
        }
    }

    buildTypes {
        release {
            isMinifyEnabled = true
            isShrinkResources = true
            proguardFiles(getDefaultProguardFile("proguard-android-optimize.txt"), "proguard-rules.pro")
            signingConfig = signingConfigs.findByName("release")
        }
        debug {
            applicationIdSuffix = ".debug"
            versionNameSuffix = "-debug"
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
    kotlinOptions { jvmTarget = "17" }
    buildFeatures {
        compose = true
        buildConfig = true
    }
    packaging { resources { excludes += "/META-INF/{AL2.0,LGPL2.1}" } }
    testOptions { unitTests.isReturnDefaultValues = true }
    lint {
        abortOnError = false
        checkReleaseBuilds = false
    }
}

dependencies {
    val composeBom = platform("androidx.compose:compose-bom:2024.12.01")
    implementation(composeBom)
    implementation("androidx.core:core-ktx:1.15.0")
    implementation("androidx.activity:activity-compose:1.9.3")
    implementation("androidx.compose.ui:ui")
    implementation("androidx.compose.material3:material3")
    implementation("androidx.compose.material:material-icons-extended")
    implementation("org.jetbrains.kotlinx:kotlinx-coroutines-android:1.9.0")
    debugImplementation("androidx.compose.ui:ui-tooling")

    testImplementation("junit:junit:4.13.2")
}
