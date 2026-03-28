#pragma once

#include <variant>

namespace furst {

// Result<T, E> — like F#'s Result<'T, 'E> or C#'s OneOf.
// Wraps std::variant since std::expected isn't available with clang 18 + libstdc++.

template <typename T, typename E> class Result {
    std::variant<T, E> data_;

  public:
    Result(T value) : data_(std::move(value)) {} // NOLINT — intentional implicit conversion
    Result(E error) : data_(std::move(error)) {} // NOLINT — intentional implicit conversion

    bool is_ok() const { return std::holds_alternative<T>(data_); }
    bool is_error() const { return std::holds_alternative<E>(data_); }

    const T& value() const { return std::get<T>(data_); }
    T& value() { return std::get<T>(data_); }

    const E& error() const { return std::get<E>(data_); }
    E& error() { return std::get<E>(data_); }
};

} // namespace furst
