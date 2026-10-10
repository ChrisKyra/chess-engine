#pragma once

#include <functional>
#include <thread>
#include <utility>

// POSIX threads wherever they exist: macOS, Linux, and MinGW on Windows (whose
// winpthreads library is linked in statically).  Only MSVC builds go without.
#if !defined(_WIN32) || defined(__MINGW32__)
#define SEARCH_THREAD_PTHREAD 1
#include <pthread.h>
#endif

// A thread with room for the deepest search.  std::thread starts a thread with
// the platform's default stack -- only 512 KB on macOS -- while a search
// MAX_PLY plies deep needs about 5 KB per ply in negamax alone, so a long
// forced line could overflow it and crash the engine.  Every searching thread
// is started through this class instead, with an 8 MB stack (only the part a
// search actually reaches is ever used).  It works like std::thread: start it
// with a function, join() it, and it can be moved but not copied.  With MSVC,
// whose default is 1 MB, it is a plain std::thread.
class SearchThread {
public:
    static constexpr size_t STACK_SIZE = 8 * 1024 * 1024;

    SearchThread() = default;
    SearchThread(const SearchThread&) = delete;
    SearchThread& operator=(const SearchThread&) = delete;
    SearchThread(SearchThread&& other) noexcept { *this = std::move(other); }

#ifndef SEARCH_THREAD_PTHREAD
    explicit SearchThread(std::function<void()> fn) : thread_(std::move(fn)) {}
    SearchThread& operator=(SearchThread&& other) noexcept {
        thread_ = std::move(other.thread_);
        return *this;
    }
    bool joinable() const { return thread_.joinable(); }
    void join() { thread_.join(); }

private:
    std::thread thread_;
#else
    // Starts fn on a new thread with STACK_SIZE of stack.
    explicit SearchThread(std::function<void()> fn) {
        pthread_attr_t attr;
        pthread_attr_init(&attr);
        pthread_attr_setstacksize(&attr, STACK_SIZE);
        auto* task = new std::function<void()>(std::move(fn));
        if (pthread_create(&thread_, &attr, run, task) == 0) {
            running_ = true;
        } else {
            // No thread could be made: run the work here rather than lose it.
            (*task)();
            delete task;
        }
        pthread_attr_destroy(&attr);
    }

    // Like std::thread, a thread still running must be joined before it is
    // replaced or destroyed; uci.cpp and search.cpp always do.
    SearchThread& operator=(SearchThread&& other) noexcept {
        thread_ = other.thread_;
        running_ = std::exchange(other.running_, false);
        return *this;
    }

    bool joinable() const { return running_; }

    // Waits for the thread to finish.
    void join() {
        if (running_) pthread_join(thread_, nullptr);
        running_ = false;
    }

private:
    // The new thread's entry point: runs the task and frees it.
    static void* run(void* arg) {
        auto* task = static_cast<std::function<void()>*>(arg);
        (*task)();
        delete task;
        return nullptr;
    }

    pthread_t thread_{};
    bool running_ = false;
#endif
};
