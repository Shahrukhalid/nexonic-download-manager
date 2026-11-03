# Keep line numbers for readable crash reports.
-keepattributes SourceFile,LineNumberTable

# The video engine reads yt-dlp's answers with Jackson (reflection): keep its classes and fields.
-keep class com.yausername.** { *; }
-keep class com.fasterxml.jackson.** { *; }
-keepattributes *Annotation*,Signature,InnerClasses,EnclosingMethod
-dontwarn com.fasterxml.jackson.**
-dontwarn org.apache.commons.**
-dontwarn java.beans.**
